using System.Net;
using System.Security.Cryptography;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Configuration;
using System.Text.Json;
using EduCenterOS.Api.ErrorHandling;
using EduCenterOS.Api.Middleware;
using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class RuntimeSecurityTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    private static AuthenticationRuntimeSettings AuthenticationSettings()
    {
        using var key = RSA.Create(2048);

        return AuthenticationRuntimeSettings.FromSnapshot(
            JsonSerializer.Serialize(new AuthenticationPolicy(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            key.ExportPkcs8PrivateKeyPem(), "runtime-test", new Dictionary<string, string> { ["runtime-test"] = key.ExportSubjectPublicKeyInfoPem() });
    }

    private IdentityAccessRuntimeSettings Settings(bool changedKey = false, bool strictLimit = false)
    {
        var policy = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(OwnedPostgresFixture.FindRoot(), "infra/registration-policy.json")))!;

        if (strictLimit) policy["issueIpPermits"] = 1;

        return IdentityAccessRuntimeSettings.FromSnapshot(
            database.ModuleConnectionString,
            new Dictionary<string, string> { ["v1"] = Convert.ToBase64String(database.OtpKey) },
            "v1",
            Convert.ToBase64String(changedKey ? System.Security.Cryptography.RandomNumberGenerator.GetBytes(32) : database.PartitionKey),
            policy.ToJsonString(),
            database.Target,
            "Testing",
            null
        );
    }

    [Fact]
    public async Task Retention_RemovesExpiredSecurityStateAndKeepsCurrentBudgets()
    {
        await database.PrepareIdentityAsync();
        await database.ResetIdentityAsync();
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var settings = Settings();
        var crypto = new RegistrationCryptography(settings);

        await using (var context = new IdentityAccessDbContext(IdentityAccessDbContext.Options(database.ModuleConnectionString)))
        {
            var old = new EduCenterOS.Modules.IdentityAccess.Domain.VerificationTarget(crypto.TargetDigest("+201012345678"));
            old.ReserveIssue(now.AddHours(-2), 900, 3, 60);
            var current = new EduCenterOS.Modules.IdentityAccess.Domain.VerificationTarget(crypto.TargetDigest("+201112345678"));
            current.ReserveIssue(now, 900, 3, 60);
            context.Targets.AddRange(old, current);
            context.Challenges.Add(new EduCenterOS.Modules.IdentityAccess.Domain.OtpChallenge(Guid.CreateVersion7(), "+201012345678", new byte[32], "v1", now.AddHours(-2), 300));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var cleanup = new RegistrationStateCleanup(settings, new ControlledClock(now), Microsoft.Extensions.Logging.Abstractions.NullLogger<RegistrationStateCleanup>.Instance);

        using (cleanup) await cleanup.SweepAsync(TestContext.Current.CancellationToken);

        await using var final = new IdentityAccessDbContext(IdentityAccessDbContext.Options(database.ModuleConnectionString));
        Assert.Single(await final.Targets.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await final.Challenges.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KeyBinding_SurvivesRestartAndRejectsQuotaResetByKeyReplacement()
    {
        await database.PrepareIdentityAsync();
        var services = new ServiceCollection().AddSingleton(Settings()).AddLogging();
        services.AddSingleton(AuthenticationSettings());
        services.AddIdentityAccess("Testing", ApiProblems.WriteErrorAsync);

        using (var provider = services.BuildServiceProvider())
        {
            var guard = provider.GetServices<IHostedService>().OfType<RegistrationRuntimeGuard>().Single();
            await guard.StartAsync(TestContext.Current.CancellationToken);
            await guard.StartAsync(TestContext.Current.CancellationToken);
        }

        var changed = new ServiceCollection().AddSingleton(Settings(changedKey: true)).AddLogging();
        changed.AddSingleton(AuthenticationSettings());
        changed.AddIdentityAccess("Testing", ApiProblems.WriteErrorAsync);
        using var changedProvider = changed.BuildServiceProvider();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => changedProvider.GetServices<IHostedService>().OfType<RegistrationRuntimeGuard>().Single().StartAsync(TestContext.Current.CancellationToken));
        Assert.StartsWith("Configuration.IdentityAccessRuntimeUnavailable", failure.Message);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task NamedRateLimit_UsesSafe429AndTrustedMissingIpFallback()
    {
        await database.PrepareIdentityAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(Settings(strictLimit: true));
        builder.Services.AddSingleton(AuthenticationSettings());
        builder.Services.AddIdentityAccess("Testing", ApiProblems.WriteErrorAsync);
        await using var app = builder.Build();
        app.UseMiddleware<CorrelationMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapGet("/fixture", () => Results.Ok()).WithMetadata(new SecurityEndpointMetadata("OtpIssue")).RequireRateLimiting("OtpIssue");
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var first = await client.GetAsync("/fixture", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/fixture");
        request.Headers.Add("X-Forwarded-For", "198.51.100.7");
        using var rejected = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var text = await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(text);
        Assert.Equal("Infrastructure.RateLimitExceeded", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("urn:educenteros:problem:rate-limited", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("no-store", rejected.Headers.CacheControl?.ToString());
        Assert.Equal(rejected.Headers.GetValues("X-Correlation-Id").Single(), document.RootElement.GetProperty("correlationId").GetString());

        if (rejected.Headers.RetryAfter?.Delta is { } retry) Assert.True(retry > TimeSpan.Zero);

        Assert.DoesNotContain(rejected.Headers, header => header.Key.StartsWith("X-RateLimit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnregisteredOrMismatchedErrorCodes_FailClosedToSanitized500()
    {
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;
        context.Items[CorrelationMiddleware.ItemKey] = "fixture-correlation";
        using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.RequestServices = provider;
        await ApiProblems.WriteErrorAsync(context, new Error(
            "Unknown.Code",
            ErrorCategory.Conflict,
            "diagnostic-sensitive-marker"
        ));
        Assert.Equal(500, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var text = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("diagnostic-sensitive-marker", text, StringComparison.Ordinal);
        Assert.Contains("General.UnexpectedError", text, StringComparison.Ordinal);
    }
}
