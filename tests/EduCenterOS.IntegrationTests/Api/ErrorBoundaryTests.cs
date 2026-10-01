using System.Diagnostics;
using System.Net;
using System.Text.Json;
using EduCenterOS.Api.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EduCenterOS.IntegrationTests.Api;

public sealed class ErrorBoundaryTests
{
    [Fact]
    public async Task UnexpectedFailure_ReturnsSanitizedProblemAndSafeStructuredEvent()
    {
        const string marker = "diagnostic-sensitive-marker";
        var logs = new SafeLogCapture();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        await using var app = builder.Build();
        app.UseMiddleware<CorrelationMiddleware>();
        app.UseMiddleware<ErrorBoundaryMiddleware>();
        app.Run(_ => throw new InvalidOperationException(marker));
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var activity = new Activity("test-request").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("General.UnexpectedError", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), document.RootElement.GetProperty("correlationId").GetString());
        Assert.Matches("^[a-f0-9]{32}$", document.RootElement.GetProperty("traceId").GetString());
        Assert.False(body.Contains(marker, StringComparison.Ordinal));
        Assert.Contains(logs.Events, entry => entry.EventId == 1001 && entry.Message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        var unsafeLog = logs.Events.Any(entry => entry.Message.Contains(marker, StringComparison.Ordinal) || entry.Exception is not null);
        Assert.False(unsafeLog);
    }

    [Fact]
    public async Task ClientCancellation_PropagatesWithoutUnexpectedFailureLog()
    {
        var logs = new SafeLogCapture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestAborted = cancellation.Token };
        using var loggerFactory = new LoggerFactory([logs]);
        var middleware = new ErrorBoundaryMiddleware(_ => throw new OperationCanceledException(cancellation.Token), loggerFactory.CreateLogger<ErrorBoundaryMiddleware>());
        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));
        Assert.DoesNotContain(logs.Events, entry => entry.EventId == 1001);
    }
}
