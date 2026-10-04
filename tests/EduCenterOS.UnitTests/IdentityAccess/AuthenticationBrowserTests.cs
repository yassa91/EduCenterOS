using EduCenterOS.Modules.IdentityAccess;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Configuration;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace EduCenterOS.UnitTests.IdentityAccess;

public sealed class AuthenticationBrowserTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("http", false)]
    [InlineData("missingOrigin", false)]
    [InlineData("multipleOrigins", false)]
    [InlineData("foreignOrigin", false)]
    [InlineData("nullOrigin", false)]
    [InlineData("missingHeader", false)]
    [InlineData("wrongHeader", false)]
    [InlineData("multipleHeaders", false)]
    public async Task BrowserMiddleware_RejectsBeforeExecutingCommand(string fault, bool permitted)
    {
        using var key = RSA.Create(2048);
        var settings = AuthenticationRuntimeSettings.FromSnapshot(
            JsonSerializer.Serialize(new AuthenticationPolicy(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            key.ExportPkcs8PrivateKeyPem(), "test", new Dictionary<string, string> { ["test"] = key.ExportSubjectPublicKeyInfoPem() });
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddLogging();
        services.AddCors(options => options.AddPolicy("AuthenticationBrowser", policy => policy.WithOrigins(settings.Policy.BrowserOrigin)
            .WithMethods("POST").WithHeaders("X-EduCenterOS-Auth", "Content-Type").AllowCredentials()));
        using var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        var calls = 0;
        string? rejectedCode = null;
        app.UseIdentityAccessBrowserSecurity((context, error) =>
        {
            rejectedCode = error.Code;
            context.Response.StatusCode = 403;

            return Task.CompletedTask;
        });
        app.Run(_ =>
        {
            calls++;

            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Scheme = "https";
        context.Request.Method = "POST";
        context.Request.Headers.Origin = settings.Policy.BrowserOrigin;
        context.Request.Headers["X-EduCenterOS-Auth"] = "1";
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new SecurityEndpointMetadata("AuthenticationSource", BrowserProtected: true)), "Test browser command"));
        if (fault == "http") context.Request.Scheme = "http";
        if (fault == "missingOrigin") context.Request.Headers.Remove("Origin");
        if (fault == "multipleOrigins") context.Request.Headers.Origin = new StringValues([settings.Policy.BrowserOrigin, settings.Policy.BrowserOrigin]);
        if (fault == "foreignOrigin") context.Request.Headers.Origin = settings.Policy.BrowserOrigin + ".evil.invalid";
        if (fault == "nullOrigin") context.Request.Headers.Origin = "null";
        if (fault == "missingHeader") context.Request.Headers.Remove("X-EduCenterOS-Auth");
        if (fault == "wrongHeader") context.Request.Headers["X-EduCenterOS-Auth"] = "true";
        if (fault == "multipleHeaders") context.Request.Headers["X-EduCenterOS-Auth"] = new StringValues(["1", "1"]);
        await app.Build()(context);
        Assert.Equal(permitted ? 1 : 0, calls);
        Assert.Equal(permitted ? null : "IdentityAccess.BrowserRequestRejected", rejectedCode);
    }

    [Fact]
    public void Cookie_UsesSecureBoundedPolicyAndMatchingDeletion()
    {
        var context = new DefaultHttpContext();
        var now = DateTimeOffset.UtcNow;
        var expiration = now.AddSeconds(301.4);
        AuthenticationCookie.Set(context, AuthenticationTokens.NewRefresh(), expiration, now);
        var cookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.ToString());
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/api/v1/auth", cookie.Path.Value);
        Assert.False(cookie.Domain.HasValue);
        Assert.True(cookie.Expires <= expiration);
        Assert.True(now + cookie.MaxAge <= expiration);
        context.Response.Headers.SetCookie = StringValues.Empty;
        AuthenticationCookie.Delete(context);
        var deleted = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.ToString());
        Assert.Equal(cookie.Name, deleted.Name);
        Assert.Equal(cookie.Path, deleted.Path);
        Assert.True(deleted.Secure && deleted.HttpOnly);
        Assert.Equal(cookie.SameSite, deleted.SameSite);
        Assert.True(deleted.Expires < now);
    }

    [Fact]
    public void CookieReader_RejectsDuplicatedOrNonCanonicalCredentials()
    {
        var context = new DefaultHttpContext();
        var raw = AuthenticationTokens.NewRefresh();
        context.Request.Headers.Cookie = AuthenticationCookie.Name + "=" + raw;
        Assert.True(AuthenticationCookie.Read(context) == raw, "A canonical single refresh credential must be read.");
        context.Request.Headers.Cookie = AuthenticationCookie.Name + "=" + raw + "; " + AuthenticationCookie.Name + "=" + raw;
        Assert.Null(AuthenticationCookie.Read(context));
        context.Request.Headers.Cookie = AuthenticationCookie.Name + "=%41";
        Assert.Null(AuthenticationCookie.Read(context));
    }
}
