using System.Text.Json;
using EduCenterOS.Api.Configuration;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EduCenterOS.UnitTests;

public sealed class SwaggerUiTests
{
    [Fact]
    public async Task Development_BootstrapInterceptor_BindsTheRequestParameter()
    {
        var script = await ServeAsync("Development", "/swagger/index.js");
        Assert.Equal(200, script.Status);
        var start = script.Body.IndexOf("var interceptors = JSON.parse('", StringComparison.Ordinal);
        Assert.True(start >= 0, "Swagger must emit the configured interceptor.");
        start += "var interceptors = JSON.parse('".Length;
        var end = script.Body.IndexOf("');", start, StringComparison.Ordinal);
        using var interceptors = JsonDocument.Parse(script.Body[start..end]);
        var function = interceptors.RootElement.GetProperty("RequestInterceptorFunction").GetString()!;

        // Match the parameter extraction in the locally served Swagger parseFunction.
        // A bare arrow parameter becomes an empty argument list and throws on the OpenAPI fetch.
        var declaration = function[..function.IndexOf('{')];
        var open = declaration.IndexOf('(');
        var close = declaration.LastIndexOf(')');
        Assert.True(open >= 0 && close > open, "Swagger's parser requires parenthesized parameters.");
        Assert.Equal("request", declaration[(open + 1)..close].Trim());
        Assert.Contains("/openapi/v1.json", script.Body, StringComparison.Ordinal);
        Assert.Contains("\"persistAuthorization\":false", script.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger/index.js")]
    [InlineData("/swagger/swagger-ui-bundle.js")]
    [InlineData("/swagger/swagger-ui.css")]
    public async Task SwaggerAssets_AreServedOnlyInDevelopment(string path)
    {
        Assert.Equal(200, (await ServeAsync("Development", path)).Status);
        Assert.Equal(404, (await ServeAsync("Testing", path)).Status);
    }

    private static async Task<(int Status, string Body)> ServeAsync(string environment, string path)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            Args = []
        });
        builder.Services.AddCors();
        builder.Services.AddRateLimiter(_ =>
        {
        });
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddDbContextFactory<IdentityAccessDbContext>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        await using var app = builder.Build();
        app.UseApiPipeline();
        using var scope = app.Services.CreateScope();
        using var output = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Response.Body = output;
        await ((IApplicationBuilder)app).Build()(context);
        output.Position = 0;
        using var reader = new StreamReader(output);

        return (context.Response.StatusCode, await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }
}
