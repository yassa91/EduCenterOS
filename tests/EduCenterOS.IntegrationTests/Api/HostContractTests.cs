using System.Net;
using System.Text.Json;
using EduCenterOS.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EduCenterOS.IntegrationTests.Api;

public sealed class HostContractTests : IClassFixture<OwnedPostgresFixture>
{
    private readonly OwnedPostgresFixture database;
    public HostContractTests(OwnedPostgresFixture database) => this.database = database;

    [Theory]
    [InlineData("/health/live", "GET")]
    [InlineData("/health/ready", "GET")]
    [InlineData("/health/live", "HEAD")]
    [InlineData("/health/ready", "HEAD")]
    public async Task Health_ConnectedDatabase_ReturnsSafeStatus(string route, string method)
    {
        await using var factory = new TestingApiFactory(database);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        request.Headers.Add("X-Correlation-Id", "untrusted-client-value");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Matches("^[a-f0-9]{32}$", response.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal(method == "HEAD" ? "" : "Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("/unknown/diagnostic-sensitive-marker?token=diagnostic-sensitive-marker", "GET", 404, "Http.RouteNotFound")]
    [InlineData("/health/live", "POST", 405, "Http.MethodNotAllowed")]
    [InlineData("/health/ready", "DELETE", 405, "Http.MethodNotAllowed")]
    public async Task Http_InvalidRouteOrMethod_ReturnsProblemContract(string route, string method, int status, string code)
    {
        await using var factory = new TestingApiFactory(database);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), problem.GetProperty("correlationId").GetString());
        Assert.StartsWith("urn:educenteros:problem:", problem.GetProperty("type").GetString());
        Assert.False(problem.TryGetProperty("instance", out _));
        Assert.False(body.Contains("diagnostic-sensitive-marker", StringComparison.Ordinal));
        var unsafeLog = factory.Logs.Events.Any(entry => entry.Message.Contains("diagnostic-sensitive-marker", StringComparison.Ordinal));
        Assert.False(unsafeLog);
        if (status == 405)
        {
            Assert.Contains("GET", response.Content.Headers.Allow);
            Assert.Contains("HEAD", response.Content.Headers.Allow);
        }
    }

    [Fact]
    public async Task Http_UnknownHeadRoute_ReturnsHeadersWithoutBody()
    {
        await using var factory = new TestingApiFactory(database);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Head, "/missing");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Readiness_DatabaseOutage_IsUnhealthyWhileLivenessSurvivesAndRecovers()
    {
        await using var factory = new TestingApiFactory(database);
        using var client = factory.CreateClient();
        var password = new Npgsql.NpgsqlConnectionStringBuilder(database.RuntimeConnectionString).Password!;
        await database.PauseAsync();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            using var readiness = await client.GetAsync("/health/ready", deadline.Token);
            using var liveness = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
            Assert.Equal("Unhealthy", await readiness.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
            Assert.Contains(factory.Logs.Events, entry => entry.EventId == 1002);
            var unsafeLog = factory.Logs.Events.Any(entry => entry.Message.Contains(password, StringComparison.Ordinal) || entry.Exception is not null);
            Assert.False(unsafeLog, "Operational logs must not retain credentials or exception details.");
        }
        finally
        {
            await database.UnpauseAsync();
        }
        using var recovered = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Fact]
    public void OperationalEndpoints_AreExcludedFromPublicApiDescription()
    {
        using var factory = new TestingApiFactory(database);
        _ = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith("health/", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(2, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            Assert.StartsWith("/health/", "/" + endpoint.RoutePattern.RawText?.TrimStart('/'));
            Assert.True(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription);
            Assert.Equal(new[] { "GET", "HEAD" }, endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods);
        }
    }
}
