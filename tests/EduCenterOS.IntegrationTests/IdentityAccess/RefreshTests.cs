using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Json;
using EduCenterOS.IntegrationTests.Api;
using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static EduCenterOS.IntegrationTests.IdentityAccess.AuthenticationTestSupport;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class RefreshTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    [Fact]
    public async Task Rotation_PreservesSessionAndPrimaryAuthenticationWhileAdvancingHashedLineage()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        await using var factory = new TestingApiFactory(database, clock);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var original = await ReadGrantAsync(login);
        clock.UtcNow = Now.AddSeconds(20);
        using var response = await RefreshAsync(client, original.Refresh);
        var replacement = await ReadGrantAsync(response);
        var tokens = factory.Services.GetRequiredService<AuthenticationTokens>();
        new JwtSecurityTokenHandler().ValidateToken(replacement.Access, tokens.ValidationParameters(), out _);
        Assert.True(tokens.TryReadActor(original.Access, out var originalActor));
        Assert.True(tokens.TryReadActor(replacement.Access, out var replacementActor));
        Assert.Equal(originalActor, replacementActor);
        Assert.NotEqual(new JwtSecurityTokenHandler().ReadJwtToken(original.Access).Id, new JwtSecurityTokenHandler().ReadJwtToken(replacement.Access).Id);
        await using (var persisted = Context(database))
        {
            var session = await persisted.Sessions.SingleAsync(TestContext.Current.CancellationToken);
            var ancestor = await persisted.RefreshTokens.SingleAsync(value => value.TokenHash == AuthenticationTokens.HashRefresh(original.Refresh), TestContext.Current.CancellationToken);
            var next = await persisted.RefreshTokens.SingleAsync(value => value.TokenHash == AuthenticationTokens.HashRefresh(replacement.Refresh), TestContext.Current.CancellationToken);
            Assert.Equal(session.Id, next.UserSessionId);
            Assert.Equal(next.Id, ancestor.ReplacedByTokenId);
            Assert.Equal(clock.UtcNow, ancestor.ConsumedAtUtc);
            Assert.Null(next.ConsumedAtUtc);
            Assert.Equal(Now, session.CreatedAtUtc);
            Assert.Equal(Now, session.AuthenticatedAtUtc);
            Assert.Equal(Now, session.LastPrimaryAuthenticatedAtUtc);
            Assert.Equal(clock.UtcNow, session.LastSeenAtUtc);
            Assert.Equal(clock.UtcNow.AddDays(30), session.IdleExpiresAtUtc);
            Assert.Equal(Now.AddDays(90), session.AbsoluteExpiresAtUtc);
            Assert.Equal(session.EffectiveExpiration, next.ExpiresAtUtc);
            Assert.Equal(2, session.Version);
            Assert.Equal(1, (await persisted.Accounts.SingleAsync(TestContext.Current.CancellationToken)).SecurityVersion);
        }
        clock.UtcNow = Now.AddSeconds(25);
        using var again = await RefreshAsync(client, replacement.Refresh);
        var descendant = await ReadGrantAsync(again);
        using var current = await CurrentAccountTests.MeAsync(client, descendant.Access);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        await using var final = Context(database);
        Assert.Equal(3, await final.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await final.RefreshTokens.CountAsync(value => value.ConsumedAtUtc != null, TestContext.Current.CancellationToken));
        Assert.Equal(3, (await final.Sessions.SingleAsync(TestContext.Current.CancellationToken)).Version);
        Assert.DoesNotContain(factory.Logs.Events, item => item.Message.Contains(original.Refresh, StringComparison.Ordinal) || item.Message.Contains(replacement.Refresh, StringComparison.Ordinal) || item.Message.Contains(descendant.Access, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExpiredConsumedAncestor_RevokesLiveDescendantAndPreviouslyValidatedAccess()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        await using var factory = new TestingApiFactory(database, clock, authenticationPolicy: policy =>
        {
            policy["accessLifetimeSeconds"] = 300;
            policy["idleTimeoutSeconds"] = 300;
            policy["absoluteLifetimeSeconds"] = 900;
        });
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var original = await ReadGrantAsync(login);
        clock.UtcNow = Now.AddSeconds(250);
        using var rotated = await RefreshAsync(client, original.Refresh);
        var descendant = await ReadGrantAsync(rotated);
        clock.UtcNow = Now.AddSeconds(301);
        using var before = await CurrentAccountTests.MeAsync(client, descendant.Access);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using var replay = await RefreshAsync(client, original.Refresh);
        await AssertRejectedAsync(replay);
        using var after = await CurrentAccountTests.MeAsync(client, descendant.Access);
        await AssertRejectedAsync(after);
        using var next = await RefreshAsync(client, descendant.Refresh);
        await AssertRejectedAsync(next);
        await using var final = Context(database);
        var session = await final.Sessions.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(clock.UtcNow, session.RevokedAtUtc);
        Assert.Equal("RefreshReuse", session.RevocationReason);
        Assert.Equal(2, await final.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await final.RefreshTokens.CountAsync(value => value.ConsumedAtUtc != null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InactiveEpochRevocationAndExpirationStates_DoNotConsumeOrSlideCredential()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        await using var factory = new TestingApiFactory(database, clock);
        using var client = Browser(factory);
        var id = await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        await using var context = Context(database);
        var sessionId = (await context.Sessions.SingleAsync(TestContext.Current.CancellationToken)).Id;
        var credentialId = (await context.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken)).Id;
        foreach (var fault in new[] { "suspended", "closed", "accountEpoch", "sessionEpoch", "sessionRevoked", "credentialRevoked", "credentialExpired", "idleExpired", "absoluteExpired" })
        {
            clock.UtcNow = Now;
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_accounts SET status=0,security_version=1,version=version+1 WHERE id={id}", TestContext.Current.CancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_sessions SET security_version_at_authentication=1,revoked_at_utc=NULL,revocation_reason=NULL,idle_expires_at_utc={Now.AddDays(30)},absolute_expires_at_utc={Now.AddDays(90)},version=version+1 WHERE id={sessionId}", TestContext.Current.CancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.refresh_token_records SET revoked_at_utc=NULL,expires_at_utc={Now.AddDays(30)},version=version+1 WHERE id={credentialId}", TestContext.Current.CancellationToken);
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
            if (fault == "sessionRevoked")
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_sessions SET revoked_at_utc={Now},revocation_reason='UserRequest',version=version+1 WHERE id={sessionId}", TestContext.Current.CancellationToken);
            }
            if (fault == "credentialRevoked")
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.refresh_token_records SET revoked_at_utc={Now},version=version+1 WHERE id={credentialId}", TestContext.Current.CancellationToken);
            }
            if (fault == "credentialExpired")
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.refresh_token_records SET expires_at_utc={Now.AddSeconds(1)},version=version+1 WHERE id={credentialId}", TestContext.Current.CancellationToken);
                clock.UtcNow = Now.AddSeconds(1);
            }
            if (fault == "idleExpired")
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_sessions SET idle_expires_at_utc={Now.AddSeconds(600)},version=version+1 WHERE id={sessionId}", TestContext.Current.CancellationToken);
                clock.UtcNow = Now.AddSeconds(600);
            }
            if (fault == "absoluteExpired")
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_sessions SET idle_expires_at_utc={Now.AddSeconds(600)},absolute_expires_at_utc={Now.AddSeconds(600)},version=version+1 WHERE id={sessionId}", TestContext.Current.CancellationToken);
                clock.UtcNow = Now.AddSeconds(600);
            }
            using var denied = await RefreshAsync(client, grant.Refresh);
            await AssertRejectedAsync(denied);
            await using var check = Context(database);
            Assert.Null((await check.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken)).ConsumedAtUtc);
            Assert.Equal(Now, (await check.Sessions.SingleAsync(TestContext.Current.CancellationToken)).LastSeenAtUtc);
            Assert.Equal(1, await check.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task StrictBodyCookieAndBrowserDenials_HappenBeforeConsumption()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now));
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        foreach (var shape in new[] { "null", "[]", JsonSerializer.Serialize(new { refreshToken = grant.Refresh }), JsonSerializer.Serialize(new { accessToken = grant.Access }), "{\"unexpected\":true}" })
        {
            using var denied = await RefreshAsync(client, grant.Refresh, shape);
            await AssertRejectedAsync(denied, HttpStatusCode.BadRequest, "Validation.Failed");
        }
        foreach (var raw in new[] { null, "invalid", AuthenticationTokens.NewRefresh(), grant.Refresh + "; __Secure-educenteros-refresh=" + grant.Refresh, "%41" + grant.Refresh[1..] })
        {
            using var denied = await RefreshAsync(client, raw, access: grant.Access);
            await AssertRejectedAsync(denied);
        }
        using (var query = await RefreshAsync(client, grant.Refresh, route: "/api/v1/auth/refresh?refreshToken=invalid"))
            await AssertRejectedAsync(query, HttpStatusCode.BadRequest, "Validation.Failed");
        using (var oversized = await RefreshAsync(client, grant.Refresh, new string('x', 16385)))
            await AssertRejectedAsync(oversized, HttpStatusCode.RequestEntityTooLarge, "Http.PayloadTooLarge");
        client.DefaultRequestHeaders.Clear();
        foreach (var fault in new[] { "http", "missingOrigin", "foreignOrigin", "duplicateOrigin", "missingMarker", "wrongMarker", "duplicateMarker" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, fault == "http" ? "http://localhost:5443/api/v1/auth/refresh" : "/api/v1/auth/refresh");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            request.Headers.Add("Cookie", "__Secure-educenteros-refresh=" + grant.Refresh);
            if (fault != "missingOrigin") request.Headers.TryAddWithoutValidation("Origin", fault == "foreignOrigin" ? "https://other.invalid" : TestingApiFactory.AuthenticationOrigin);
            if (fault == "duplicateOrigin") request.Headers.TryAddWithoutValidation("Origin", TestingApiFactory.AuthenticationOrigin);
            if (fault != "missingMarker") request.Headers.Add("X-EduCenterOS-Auth", fault == "wrongMarker" ? "true" : "1");
            if (fault == "duplicateMarker") request.Headers.Add("X-EduCenterOS-Auth", "1");
            using var denied = await client.SendAsync(request, TestContext.Current.CancellationToken);
            await AssertRejectedAsync(denied, HttpStatusCode.Forbidden, "IdentityAccess.BrowserRequestRejected");
        }
        await using var final = Context(database);
        Assert.Null((await final.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken)).ConsumedAtUtc);
        Assert.Equal(Now, (await final.Sessions.SingleAsync(TestContext.Current.CancellationToken)).LastSeenAtUtc);
    }

    [Fact]
    public async Task RefreshSourceThrottle_DoesNotConsumeReplacementOrTrustForwardedAddresses()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now), authenticationPolicy: policy => policy["refreshSourcePermits"] = 1);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var original = await ReadGrantAsync(login);
        using var first = await RefreshAsync(client, original.Refresh);
        var next = await ReadGrantAsync(first);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.123");
        using var second = await RefreshAsync(client, next.Refresh);
        await AssertRejectedAsync(second, HttpStatusCode.TooManyRequests, "IdentityAccess.AuthenticationThrottled");
        await using var final = Context(database);
        Assert.Equal(2, await final.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await final.RefreshTokens.CountAsync(value => value.ConsumedAtUtc != null, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealCommitFault_NeverReturnsCredentialsOrRetriesAndReplayFollowsDurableOutcome(bool afterCommit)
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        var fault = new AuthenticationCommitFault(afterCommit);
        await using var factory = new TestingApiFactory(database, clock, fault);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var original = await ReadGrantAsync(login);
        clock.UtcNow = Now.AddSeconds(20);
        fault.Enabled = true;
        using var lost = await RefreshAsync(client, original.Refresh);
        await AssertRejectedAsync(lost, HttpStatusCode.InternalServerError, "General.UnexpectedError");
        Assert.Null(lost.Headers.RetryAfter);
        Assert.Equal(1, fault.CommitCalls);
        await using (var observed = Context(database))
        {
            Assert.Equal(afterCommit ? 2 : 1, await observed.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(afterCommit ? clock.UtcNow : Now, (await observed.Sessions.SingleAsync(TestContext.Current.CancellationToken)).LastSeenAtUtc);
            Assert.Equal(afterCommit ? clock.UtcNow : (DateTimeOffset?)null, (await observed.RefreshTokens.SingleAsync(value => value.TokenHash == AuthenticationTokens.HashRefresh(original.Refresh), TestContext.Current.CancellationToken)).ConsumedAtUtc);
        }
        fault.Enabled = false;
        using var beforeReplay = await CurrentAccountTests.MeAsync(client, original.Access);
        Assert.Equal(HttpStatusCode.OK, beforeReplay.StatusCode);
        using var retry = await RefreshAsync(client, original.Refresh);
        if (afterCommit) await AssertRejectedAsync(retry);
        else await ReadGrantAsync(retry);
        using var afterReplay = await CurrentAccountTests.MeAsync(client, original.Access);
        if (afterCommit) await AssertRejectedAsync(afterReplay);
        else Assert.Equal(HttpStatusCode.OK, afterReplay.StatusCode);
        await using var final = Context(database);
        Assert.Equal(afterCommit ? "RefreshReuse" : null, (await final.Sessions.SingleAsync(TestContext.Current.CancellationToken)).RevocationReason);
        Assert.Equal(2, await final.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(factory.Logs.Events, item => item.Message.Contains("synthetic-auth-commit-fault", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResponseGate_WithholdsGrantUntilAtomicRotationIsVisibleOutsideTransaction()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        var gate = new AuthenticationCommitGate();
        await using var factory = new TestingApiFactory(database, clock, gate);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var original = await ReadGrantAsync(login);
        clock.UtcNow = Now.AddSeconds(20);
        gate.Enabled = true;
        var pending = RefreshAsync(client, original.Refresh);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.False(pending.IsCompleted, "A replacement must not reach the client before Commit.");
            await using var before = Context(database);
            Assert.Equal(1, await before.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
            Assert.Null((await before.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken)).ConsumedAtUtc);
            Assert.Equal(Now, (await before.Sessions.SingleAsync(TestContext.Current.CancellationToken)).LastSeenAtUtc);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        using var response = await pending;
        await ReadGrantAsync(response);
        await using var committed = Context(database);
        Assert.Equal(2, await committed.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(clock.UtcNow, (await committed.Sessions.SingleAsync(TestContext.Current.CancellationToken)).LastSeenAtUtc);
    }

    [Fact]
    public async Task CancellationBeforeRealCommit_RollsBackRotationAndReleasesAuthoritativeClaim()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        var gate = new AuthenticationCommitGate();
        await using var factory = new TestingApiFactory(database, clock, gate);
        using var client = Browser(factory);
        var accountId = await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var original = await ReadGrantAsync(login);
        clock.UtcNow = Now.AddSeconds(20);
        gate.Enabled = true;
        using var abort = new CancellationTokenSource();
        var pending = RefreshAsync(client, original.Refresh, cancellation: abort.Token);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            abort.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            await using var observed = Context(database);
            await using var transaction = await observed.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            // Acquiring the same owner row proves the aborted request has released its transaction.
            await EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence.AuthenticationTransactions.LockAccountAsync(observed, accountId, TestContext.Current.CancellationToken);
            Assert.Equal(1, await observed.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
            Assert.Null((await observed.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken)).ConsumedAtUtc);
            Assert.Equal(Now, (await observed.Sessions.SingleAsync(TestContext.Current.CancellationToken)).LastSeenAtUtc);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            abort.Cancel();
            gate.Release.TrySetResult();
        }
        gate.Enabled = false;
        using var retried = await RefreshAsync(client, original.Refresh);
        await ReadGrantAsync(retried);
    }

    [Fact]
    public async Task SigningFailure_RollsBackConsumptionLineageAndActivity()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        await using var factory = new TestingApiFactory(database, clock);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var original = await ReadGrantAsync(login);
        clock.UtcNow = Now.AddSeconds(20);
        factory.Services.GetRequiredService<AuthenticationTokens>().Dispose();
        using var failure = await RefreshAsync(client, original.Refresh);
        await AssertRejectedAsync(failure, HttpStatusCode.InternalServerError, "General.UnexpectedError");
        await using var final = Context(database);
        Assert.Null((await final.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken)).ConsumedAtUtc);
        Assert.Equal(Now, (await final.Sessions.SingleAsync(TestContext.Current.CancellationToken)).LastSeenAtUtc);
        Assert.Equal(1, await final.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SameCredentialOverlap_ObservesPostgresBlockingThenOneRotationAndDurableReuseRevocation()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        var gate = new AuthenticationCommitGate();
        var probe = new AuthenticationAccountLockProbe { Enabled = false };
        await using var factory = new TestingApiFactory(database, clock, gate, configureServices: services => services.AddSingleton<IInterceptor>(probe));
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var original = await ReadGrantAsync(login);
        clock.UtcNow = Now.AddSeconds(20);
        await using var observer = new NpgsqlConnection(database.AdminConnectionString);
        await observer.OpenAsync(TestContext.Current.CancellationToken);
        probe.Enabled = true;
        gate.Enabled = true;
        var first = RefreshAsync(client, original.Refresh);
        Task<HttpResponseMessage>? second = null;
        HttpResponseMessage[] responses = [];
        var observedOverlap = false;
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            second = RefreshAsync(client, original.Refresh);
            var backend = await probe.SecondBackendPid.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            var blocked = false;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (!blocked && DateTimeOffset.UtcNow < deadline)
            {
                await using var query = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid))", observer) { CommandTimeout = 5 };
                query.Parameters.AddWithValue("pid", backend);
                blocked = (int)(await query.ExecuteScalarAsync(TestContext.Current.CancellationToken))! > 0;
            }
            Assert.True(blocked, "Independent refresh requests must contend on the owning account row.");
            observedOverlap = true;
        }
        finally
        {
            gate.Release.TrySetResult();
            responses = second is null ? [await first] : await Task.WhenAll(first, second);
            if (!observedOverlap)
                foreach (var response in responses) response.Dispose();
        }
        TestAuthenticationGrant? rotated = null;
        try
        {
            rotated = await ReadGrantAsync(responses[0]);
            await AssertRejectedAsync(responses[1]);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
        Assert.Equal(2, probe.Contexts.Distinct().Count());
        Assert.NotNull(rotated);
        using var bearer = await CurrentAccountTests.MeAsync(client, rotated.Access);
        await AssertRejectedAsync(bearer);
        using var descendant = await RefreshAsync(client, rotated.Refresh);
        await AssertRejectedAsync(descendant);
        await using var final = Context(database);
        Assert.Equal(2, await final.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await final.RefreshTokens.CountAsync(value => value.ConsumedAtUtc != null, TestContext.Current.CancellationToken));
        Assert.Equal("RefreshReuse", (await final.Sessions.SingleAsync(TestContext.Current.CancellationToken)).RevocationReason);
    }

    [Fact]
    public async Task FractionalAbsoluteDeadline_RejectsUntransportableGrantWithoutConsumingOrSliding()
    {
        await PrepareAsync(database);
        var start = Now.AddMilliseconds(500);
        var clock = new ControlledClock(start);
        await using var factory = new TestingApiFactory(database, clock, authenticationPolicy: policy =>
        {
            policy["accessLifetimeSeconds"] = 30;
            policy["idleTimeoutSeconds"] = 300;
            policy["absoluteLifetimeSeconds"] = 300;
        });
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var original = await ReadGrantAsync(login);
        clock.UtcNow = Now.AddMilliseconds(299500);
        using var denied = await RefreshAsync(client, original.Refresh);
        await AssertRejectedAsync(denied);
        await using var final = Context(database);
        Assert.Null((await final.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken)).ConsumedAtUtc);
        Assert.Equal(start, (await final.Sessions.SingleAsync(TestContext.Current.CancellationToken)).LastSeenAtUtc);
        Assert.Equal(1, await final.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
    }

    internal static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string? raw, string body = "{}", string? access = null, string route = "/api/v1/auth/refresh", CancellationToken? cancellation = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (raw is not null) request.Headers.TryAddWithoutValidation("Cookie", "__Secure-educenteros-refresh=" + raw);
        if (access is not null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + access);

        return await client.SendAsync(request, cancellation ?? TestContext.Current.CancellationToken);
    }
}
