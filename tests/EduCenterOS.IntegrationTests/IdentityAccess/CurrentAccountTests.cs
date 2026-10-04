using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduCenterOS.IntegrationTests.Api;
using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using static EduCenterOS.IntegrationTests.IdentityAccess.AuthenticationTestSupport;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class CurrentAccountTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    private const string Route = "/api/v1/accounts/me";

    [Fact]
    public async Task ProductionLoginAndBearer_ExposeOnlyOwnProjectionAndIsolateConcurrentActors()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        await using var factory = new TestingApiFactory(database, clock);
        using var client = Browser(factory);
        var firstId = await SeedAsync(database, factory, fullName: "الحساب الأول");
        var secondId = await SeedAsync(database, factory, "01112345678", fullName: "الحساب الثاني");
        using var firstLogin = await LoginAsync(client);
        var first = await ReadGrantAsync(firstLogin);
        using var secondLogin = await LoginAsync(client, "01112345678");
        var second = await ReadGrantAsync(secondLogin);
        clock.UtcNow = Now.AddSeconds(10);
        var responses = await Task.WhenAll(MeAsync(client, first.Access), MeAsync(client, second.Access));
        try
        {
            for (var index = 0; index < responses.Length; index++)
            {
                Assert.Equal(HttpStatusCode.OK, responses[index].StatusCode);
                Assert.Equal("no-store", responses[index].Headers.CacheControl?.ToString());
                using var json = JsonDocument.Parse(await responses[index].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                Assert.Equal(new[] { "createdAtUtc", "fullName", "personIdentityId", "userAccountId" }, json.RootElement.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).ToArray());
                Assert.Equal(index == 0 ? firstId : secondId, json.RootElement.GetProperty("userAccountId").GetGuid());
                Assert.Equal(index == 0 ? "الحساب الأول" : "الحساب الثاني", json.RootElement.GetProperty("fullName").GetString());
            }
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
        await using var context = Context(database);
        Assert.All(await context.Sessions.ToListAsync(TestContext.Current.CancellationToken), session =>
        {
            Assert.Equal(Now, session.LastSeenAtUtc);
            Assert.Equal(Now.AddDays(30), session.IdleExpiresAtUtc);
            Assert.Equal(1, session.Version);
        });
        Assert.DoesNotContain(factory.Logs.Events, entry => entry.Message.Contains(first.Access, StringComparison.Ordinal) || entry.Message.Contains(second.Access, StringComparison.Ordinal));
        using var query = await MeAsync(client, first.Access, Route + "?userAccountId=" + secondId.ToString("D"));
        await AssertRejectedAsync(query, HttpStatusCode.BadRequest, "Validation.Failed");
        using var unsupportedRequest = new HttpRequestMessage(HttpMethod.Get, Route);
        unsupportedRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer " + first.Access);
        unsupportedRequest.Headers.Accept.ParseAdd("application/xml");
        using var unsupported = await client.SendAsync(unsupportedRequest, TestContext.Current.CancellationToken);
        await AssertRejectedAsync(unsupported, HttpStatusCode.NotAcceptable, "Http.NotAcceptable");
    }

    [Fact]
    public async Task CookieOrMalformedBearer_DoesNotAuthenticateAndAnonymousRoutesRemainAvailable()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now));
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        foreach (var fault in new[] { "missing", "cookieOnly", "basic", "empty", "duplicate", "oversized", "whitespace", "http" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, fault == "http" ? "http://localhost:5443" + Route : Route);
            if (fault == "cookieOnly") request.Headers.Add("Cookie", "__Secure-educenteros-refresh=" + grant.Refresh);
            else if (fault != "missing")
            {
                var header = fault switch
                {
                    "basic" => "Basic " + grant.Access,
                    "empty" => "Bearer ",
                    "oversized" => "Bearer " + new string('x', 8193),
                    "whitespace" => "Bearer " + grant.Access + " x",
                    _ => "Bearer " + grant.Access
                };
                request.Headers.TryAddWithoutValidation("Authorization", fault == "duplicate" ? new[] { header, header } : new[] { header });
            }
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            await AssertRejectedAsync(response);
        }
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer invalid-optional-token");
        using var openapi = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, openapi.StatusCode);
        using var health = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var unknown = await client.GetAsync("/api/v1/unimplemented", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var anonymous = await LoginAsync(client, "01512345678");
        await AssertRejectedAsync(anonymous);
        await using var context = Context(database);
        Assert.Equal(2, await context.LoginTargets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProductionValidator_RejectsCryptographicAndStrictProfileFaultsWithoutEchoingTokens()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now));
        using var client = Browser(factory);
        var accountId = await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        await using var context = Context(database);
        var sessionId = (await context.Sessions.SingleAsync(TestContext.Current.CancellationToken)).Id;
        var settings = factory.Services.GetRequiredService<AuthenticationRuntimeSettings>();
        foreach (var fault in new[] { "signature", "kid", "algorithm", "type", "issuer", "audience", "expired", "futureIssued", "extraClaim", "duplicateClaim", "stringVersion", "emptyAccount", "noncanonicalAccount", "emptySession", "emptyJti", "zeroVersion", "notBefore", "remoteKey", "reversedExpiry", "oversizedLifetime", "nonexistentSession" })
        {
            var encoded = FaultToken(settings, accountId, sessionId, fault);
            using var response = await MeAsync(client, encoded);
            await AssertRejectedAsync(response);
            Assert.DoesNotContain(encoded, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
            Assert.DoesNotContain(factory.Logs.Events, entry => entry.Message.Contains(encoded, StringComparison.Ordinal));
        }
        using var accepted = await MeAsync(client, grant.Access);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task AuthoritativeState_RejectsNextRequestAfterAccountOrSessionInvalidation()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now));
        using var client = Browser(factory);
        var id = await SeedAsync(database, factory);
        var other = await SeedAsync(database, factory, "01112345678");
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        await using var context = Context(database);
        var sessionId = (await context.Sessions.SingleAsync(TestContext.Current.CancellationToken)).Id;
        foreach (var fault in new[] { "suspended", "closed", "accountEpoch", "sessionEpoch", "ownership", "revoked" })
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_accounts SET status=0,security_version=1,version=version+1 WHERE id={id}", TestContext.Current.CancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_sessions SET user_account_id={id},security_version_at_authentication=1,revoked_at_utc=NULL,revocation_reason=NULL,version=version+1 WHERE id={sessionId}", TestContext.Current.CancellationToken);
            if (fault == "suspended")
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_accounts SET status=1,version=version+1 WHERE id={id}", TestContext.Current.CancellationToken);
            }
            if (fault == "closed")
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_accounts SET status=2,version=version+1 WHERE id={id}", TestContext.Current.CancellationToken);
            }
            if (fault == "accountEpoch")
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_accounts SET security_version=2,version=version+1 WHERE id={id}", TestContext.Current.CancellationToken);
            }
            if (fault == "sessionEpoch")
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_sessions SET security_version_at_authentication=2,version=version+1 WHERE id={sessionId}", TestContext.Current.CancellationToken);
            }
            if (fault == "ownership")
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_sessions SET user_account_id={other},version=version+1 WHERE id={sessionId}", TestContext.Current.CancellationToken);
            }
            if (fault == "revoked")
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_sessions SET revoked_at_utc={Now},revocation_reason='UserRequest',version=version+1 WHERE id={sessionId}", TestContext.Current.CancellationToken);
            }
            using var denied = await MeAsync(client, grant.Access);
            await AssertRejectedAsync(denied);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionDeadline_IsAuthoritativeAtExactBoundaryDespiteJwtSkew(bool absolute)
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        await using var factory = new TestingApiFactory(database, clock, authenticationPolicy: policy =>
        {
            policy["idleTimeoutSeconds"] = 600;
            policy["absoluteLifetimeSeconds"] = absolute ? 600 : 900;
        });
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        clock.UtcNow = Now.AddSeconds(599).AddTicks(9999990);
        using var before = await MeAsync(client, grant.Access);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        clock.UtcNow = Now.AddSeconds(600);
        using var at = await MeAsync(client, grant.Access);
        await AssertRejectedAsync(at);
        await using var context = Context(database);
        Assert.Equal(Now, (await context.Sessions.SingleAsync(TestContext.Current.CancellationToken)).LastSeenAtUtc);
    }

    [Fact]
    public async Task JwtExpiry_UsesInjectedClockAndRejectsAtSkewBoundaryWithoutSessionMutation()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        await using var factory = new TestingApiFactory(database, clock);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        clock.UtcNow = Now.AddSeconds(604);
        using var withinSkew = await MeAsync(client, grant.Access);
        Assert.Equal(HttpStatusCode.OK, withinSkew.StatusCode);
        clock.UtcNow = Now.AddSeconds(605);
        using var expired = await MeAsync(client, grant.Access);
        await AssertRejectedAsync(expired);
        await using var context = Context(database);
        Assert.Equal(1, (await context.Sessions.SingleAsync(TestContext.Current.CancellationToken)).Version);
    }

    [Fact]
    public async Task ProtectedSourceBudget_UsesRegisteredAuthenticationThrottleWithoutActivityWrites()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now), authenticationPolicy: policy => policy["sourcePermits"] = 2);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        using var allowed = await MeAsync(client, grant.Access);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var throttled = await MeAsync(client, grant.Access);
        await AssertRejectedAsync(throttled, HttpStatusCode.TooManyRequests, "IdentityAccess.AuthenticationThrottled");
        await using var context = Context(database);
        Assert.Equal(1, (await context.Sessions.SingleAsync(TestContext.Current.CancellationToken)).Version);
    }

    [Fact]
    public async Task DatabaseFailure_FailsClosedWithSafeInfrastructureErrorInsteadOfAuthenticationRejection()
    {
        await PrepareAsync(database);
        var fault = new CurrentSessionReadFault();
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now), fault);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        fault.Enabled = true;
        using var denied = await MeAsync(client, grant.Access);
        await AssertRejectedAsync(denied, HttpStatusCode.InternalServerError, "General.UnexpectedError");
        Assert.Empty(denied.Headers.WwwAuthenticate);
        Assert.DoesNotContain(factory.Logs.Events, item => item.Message.Contains("synthetic-session-read-fault", StringComparison.Ordinal));
        fault.Enabled = false;
        using var recovered = await MeAsync(client, grant.Access);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    internal static async Task<HttpResponseMessage> MeAsync(HttpClient client, string access, string route = Route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + access);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string FaultToken(AuthenticationRuntimeSettings settings, Guid account, Guid session, string fault)
    {
        var header = new Dictionary<string, object> { ["alg"] = "RS256", ["kid"] = settings.CurrentKeyId, ["typ"] = "educenteros-access+jwt" };
        var payload = new Dictionary<string, object>
        {
            ["iss"] = settings.Policy.Issuer, ["aud"] = settings.Policy.Audience,
            ["sub"] = account.ToString("D"), ["sid"] = session.ToString("D"), ["jti"] = Guid.NewGuid().ToString("D"),
            ["sv"] = 1L, ["iat"] = Now.ToUnixTimeSeconds(), ["exp"] = Now.AddSeconds(600).ToUnixTimeSeconds()
        };
        switch (fault)
        {
            case "kid":
                header["kid"] = "unknown";
                break;
            case "algorithm":
                header["alg"] = "HS256";
                break;
            case "type":
                header["typ"] = "JWT";
                break;
            case "issuer":
                payload["iss"] = "https://other.invalid";
                break;
            case "audience":
                payload["aud"] = "other";
                break;
            case "expired":
                payload["exp"] = Now.AddSeconds(-6).ToUnixTimeSeconds();
                break;
            case "futureIssued":
                payload["iat"] = Now.AddSeconds(6).ToUnixTimeSeconds();
                break;
            case "extraClaim":
                payload["role"] = "Owner";
                break;
            case "stringVersion":
                payload["sv"] = "1";
                break;
            case "emptyAccount":
                payload["sub"] = Guid.Empty.ToString("D");
                break;
            case "noncanonicalAccount":
                payload["sub"] = account.ToString("D").ToUpperInvariant();
                break;
            case "emptySession":
                payload["sid"] = Guid.Empty.ToString("D");
                break;
            case "emptyJti":
                payload["jti"] = Guid.Empty.ToString("D");
                break;
            case "zeroVersion":
                payload["sv"] = 0L;
                break;
            case "notBefore":
                payload["nbf"] = Now.ToUnixTimeSeconds();
                break;
            case "remoteKey":
                header["jku"] = "https://other.invalid/keys";
                break;
            case "reversedExpiry":
                payload["exp"] = Now.ToUnixTimeSeconds();
                break;
            case "oversizedLifetime":
                payload["exp"] = Now.AddSeconds(901).ToUnixTimeSeconds();
                break;
            case "nonexistentSession":
                payload["sid"] = Guid.NewGuid().ToString("D");
                break;
        }
        var json = JsonSerializer.Serialize(payload);
        if (fault == "duplicateClaim") json = json[..^1] + ",\"sv\":1}";
        var signingInput = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header))) + "." + Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
        using var key = RSA.Create(2048);
        if (fault != "signature") key.ImportFromPem(settings.PrivateKeyPem);
        var signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return signingInput + "." + Base64UrlEncoder.Encode(signature);
    }
}

internal sealed class CurrentSessionReadFault : DbCommandInterceptor
{
    internal bool Enabled { get; set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && command.CommandText.Contains("user_sessions", StringComparison.Ordinal) && command.CommandText.Contains("JOIN", StringComparison.Ordinal))
            throw new IOException("synthetic-session-read-fault");

        return ValueTask.FromResult(result);
    }
}
