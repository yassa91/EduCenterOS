using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using EduCenterOS.IntegrationTests.Api;
using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Npgsql;
using Xunit;
using static EduCenterOS.IntegrationTests.IdentityAccess.AuthenticationTestSupport;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class SessionManagementTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    [Fact]
    public async Task SessionPages_AreOwnBoundedDeterministicHistoryWithStrictQueriesAndNoSliding()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        await using var factory = new TestingApiFactory(database, clock);
        using var client = Browser(factory);
        var account = await SeedAsync(database, factory);
        await SeedAsync(database, factory, "01112345678");
        var grants = new List<TestAuthenticationGrant>();
        for (var index = 0; index < 3; index++)
        {
            using var login = await LoginAsync(client);
            grants.Add(await ReadGrantAsync(login));
        }
        using var otherLogin = await LoginAsync(client, "01112345678");
        var foreign = await ReadGrantAsync(otherLogin);
        var tokens = factory.Services.GetRequiredService<AuthenticationTokens>();
        Assert.True(tokens.TryReadActor(grants[0].Access, out var revoked));
        Assert.True(tokens.TryReadActor(grants[1].Access, out var expired));
        Assert.True(tokens.TryReadActor(grants[2].Access, out var current));
        Assert.True(tokens.TryReadActor(foreign.Access, out var other));
        await using (var setup = Context(database))
        {
            await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_sessions SET revoked_at_utc={Now},revocation_reason='UserRequest',version=version+1 WHERE id={revoked.SessionId}", TestContext.Current.CancellationToken);
            await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_sessions SET idle_expires_at_utc={Now.AddSeconds(1)},version=version+1 WHERE id={expired.SessionId}", TestContext.Current.CancellationToken);
        }
        clock.UtcNow = Now.AddSeconds(2);
        var ids = new List<Guid>();
        var currentCount = 0;
        for (var page = 1; page <= 2; page++)
        {
            using var response = await GetAsync(client, grants[2].Access, $"/api/v1/auth/sessions?page={page}&pageSize=2");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(new[] { "items", "pagination" }, json.RootElement.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal).ToArray());
            var pagination = json.RootElement.GetProperty("pagination");
            Assert.Equal("page", pagination.GetProperty("type").GetString());
            Assert.Equal(page, pagination.GetProperty("page").GetInt32());
            Assert.Equal(2, pagination.GetProperty("pageSize").GetInt32());
            Assert.Equal(3, pagination.GetProperty("totalCount").GetInt64());
            Assert.Equal(2, pagination.GetProperty("totalPages").GetInt64());
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                Assert.Equal(new[] { "absoluteExpiresAtUtc", "authenticatedAtUtc", "createdAtUtc", "idleExpiresAtUtc", "isCurrent", "lastSeenAtUtc", "revokedAtUtc", "sessionId" }, item.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal).ToArray());
                var id = item.GetProperty("sessionId").GetGuid();
                ids.Add(id);
                if (item.GetProperty("isCurrent").GetBoolean())
                {
                    currentCount++;
                    Assert.Equal(current.SessionId, id);
                }
                if (id == revoked.SessionId) Assert.Equal(Now, item.GetProperty("revokedAtUtc").GetDateTimeOffset());
                if (id == expired.SessionId) Assert.Equal(Now.AddSeconds(1), item.GetProperty("idleExpiresAtUtc").GetDateTimeOffset());
            }
        }
        var expected = new[] { revoked.SessionId, expired.SessionId, current.SessionId }.OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, ids);
        Assert.Equal(1, currentCount);
        Assert.DoesNotContain(other.SessionId, ids);
        using (var defaults = await GetAsync(client, grants[2].Access, "/api/v1/auth/sessions"))
        {
            Assert.Equal(HttpStatusCode.OK, defaults.StatusCode);
            using var body = JsonDocument.Parse(await defaults.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(1, body.RootElement.GetProperty("pagination").GetProperty("page").GetInt32());
            Assert.Equal(20, body.RootElement.GetProperty("pagination").GetProperty("pageSize").GetInt32());
        }
        using (var huge = await GetAsync(client, grants[2].Access, "/api/v1/auth/sessions?page=2147483647&pageSize=100"))
        {
            Assert.Equal(HttpStatusCode.OK, huge.StatusCode);
            using var body = JsonDocument.Parse(await huge.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(3, body.RootElement.GetProperty("pagination").GetProperty("totalCount").GetInt64());
        }
        foreach (var query in new[] { "page=0", "page=-1", "page=abc", "page=%2B1", "page=%201", "page=١", "page=2147483648", "page=1&page=2", "pageSize=0", "pageSize=101", "pageSize=1&pageSize=2", "Page=1", "accountId=" + account.ToString("D") })
        {
            using var rejected = await GetAsync(client, grants[2].Access, "/api/v1/auth/sessions?" + query);
            await AssertRejectedAsync(rejected, HttpStatusCode.BadRequest, "Validation.Failed");
        }
        using (var invalid = await GetAsync(client, grants[2].Access, "/api/v1/auth/sessions?page=0"))
        {
            using var body = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("query.page", out _));
        }
        await using var final = Context(database);
        Assert.All(await final.Sessions.ToListAsync(TestContext.Current.CancellationToken), session => Assert.Equal(Now, session.LastSeenAtUtc));
    }

    [Fact]
    public async Task RevokeTarget_HidesForeignMissingResourcesPreservesOtherCookieAndIsTerminal()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now));
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        await SeedAsync(database, factory, "01112345678");
        using var firstLogin = await LoginAsync(client);
        var first = await ReadGrantAsync(firstLogin);
        using var secondLogin = await LoginAsync(client);
        var second = await ReadGrantAsync(secondLogin);
        using var otherLogin = await LoginAsync(client, "01112345678");
        var other = await ReadGrantAsync(otherLogin);
        var tokens = factory.Services.GetRequiredService<AuthenticationTokens>();
        Assert.True(tokens.TryReadActor(first.Access, out var firstActor));
        Assert.True(tokens.TryReadActor(second.Access, out var secondActor));
        Assert.True(tokens.TryReadActor(other.Access, out var otherActor));
        using (var revoke = await CommandAsync(client, RevokeRoute(firstActor.SessionId), second.Access, second.Refresh))
            await AssertNoContentAsync(revoke, false);
        using (var original = await MeAsync(client, first.Access)) await AssertRejectedAsync(original);
        using (var active = await MeAsync(client, second.Access)) Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        long version;
        await using (var before = Context(database)) version = (await before.Sessions.SingleAsync(session => session.Id == firstActor.SessionId, TestContext.Current.CancellationToken)).Version;
        using (var repeat = await CommandAsync(client, RevokeRoute(firstActor.SessionId), second.Access, first.Refresh))
            await AssertNoContentAsync(repeat, true);
        await using (var after = Context(database)) Assert.Equal(version, (await after.Sessions.SingleAsync(session => session.Id == firstActor.SessionId, TestContext.Current.CancellationToken)).Version);
        string? hiddenDetail = null;
        foreach (var id in new[] { Guid.NewGuid(), otherActor.SessionId })
        {
            using var hidden = await CommandAsync(client, RevokeRoute(id), second.Access, second.Refresh);
            await AssertRejectedAsync(hidden, HttpStatusCode.NotFound, "IdentityAccess.SessionNotFound");
            using var body = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            hiddenDetail ??= body.RootElement.GetProperty("detail").GetString();
            Assert.Equal(hiddenDetail, body.RootElement.GetProperty("detail").GetString());
        }
        foreach (var id in new[] { "invalid", Guid.Empty.ToString("D"), "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA" })
        {
            using var malformed = await CommandAsync(client, "/api/v1/auth/sessions/" + id + "/revoke", second.Access, second.Refresh);
            await AssertRejectedAsync(malformed, HttpStatusCode.BadRequest, "Validation.Failed");
            using var body = JsonDocument.Parse(await malformed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("route.sessionId", out _));
        }
        using (var current = await CommandAsync(client, RevokeRoute(secondActor.SessionId), second.Access, second.Refresh))
            await AssertNoContentAsync(current, true);
        using (var revoked = await MeAsync(client, second.Access)) await AssertRejectedAsync(revoked);
        using (var refresh = await RefreshAsync(client, second.Refresh)) await AssertRejectedAsync(refresh);
        using var untouched = await MeAsync(client, other.Access);
        Assert.Equal(HttpStatusCode.OK, untouched.StatusCode);
        await using var final = Context(database);
        Assert.All(await final.Accounts.ToListAsync(TestContext.Current.CancellationToken), account => Assert.Equal(1, account.SecurityVersion));
    }

    [Fact]
    public async Task LogoutCookie_ResolvesExpiredConsumedAncestorWithoutLiveAccessAndClearsNoOps()
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
        using var renewed = await RefreshAsync(client, original.Refresh);
        var newer = await ReadGrantAsync(renewed);
        clock.UtcNow = Now.AddSeconds(306);
        using (var live = await MeAsync(client, newer.Access)) Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using (var logout = await CommandAsync(client, "/api/v1/auth/logout", original.Access, original.Refresh))
            await AssertNoContentAsync(logout, true);
        using (var revoked = await MeAsync(client, newer.Access)) await AssertRejectedAsync(revoked);
        using (var refresh = await RefreshAsync(client, newer.Refresh)) await AssertRejectedAsync(refresh);
        foreach (var cookie in new[] { null, "invalid", AuthenticationTokens.NewRefresh(), original.Refresh })
        {
            using var noop = await CommandAsync(client, "/api/v1/auth/logout", "invalid-optional-access", cookie);
            await AssertNoContentAsync(noop, true);
        }
        await using var final = Context(database);
        Assert.Equal("LogoutCurrent", (await final.Sessions.SingleAsync(TestContext.Current.CancellationToken)).RevocationReason);
        Assert.Equal(1, (await final.Accounts.SingleAsync(TestContext.Current.CancellationToken)).SecurityVersion);
    }

    [Fact]
    public async Task LogoutAll_RevokesOnlyOwnExistingHistoryWithoutEpochChangeAndAllowsLaterLogin()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        await using var factory = new TestingApiFactory(database, clock);
        using var client = Browser(factory);
        var id = await SeedAsync(database, factory);
        await SeedAsync(database, factory, "01112345678");
        var grants = new List<TestAuthenticationGrant>();
        for (var index = 0; index < 3; index++)
        {
            using var login = await LoginAsync(client);
            grants.Add(await ReadGrantAsync(login));
        }
        using var otherLogin = await LoginAsync(client, "01112345678");
        var other = await ReadGrantAsync(otherLogin);
        clock.UtcNow = Now.AddSeconds(10);
        using (var logout = await CommandAsync(client, "/api/v1/auth/logout-all", grants[0].Access, grants[2].Refresh))
            await AssertNoContentAsync(logout, true);
        foreach (var grant in grants)
        {
            using var revoked = await MeAsync(client, grant.Access);
            await AssertRejectedAsync(revoked);
        }
        using (var untouched = await MeAsync(client, other.Access)) Assert.Equal(HttpStatusCode.OK, untouched.StatusCode);
        await using (var observed = Context(database))
        {
            var account = await observed.Accounts.SingleAsync(value => value.Id == id, TestContext.Current.CancellationToken);
            Assert.Equal(1, account.SecurityVersion);
            Assert.Equal(Now, account.PasswordChangedAtUtc);
            Assert.Equal(3, await observed.Sessions.CountAsync(session => session.UserAccountId == id && session.RevokedAtUtc != null, TestContext.Current.CancellationToken));
        }
        using var laterLogin = await LoginAsync(client);
        var later = await ReadGrantAsync(laterLogin);
        using var accepted = await MeAsync(client, later.Access);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await using var final = Context(database);
        Assert.Equal(1, await final.Sessions.CountAsync(session => session.UserAccountId == id && session.RevokedAtUtc == null, TestContext.Current.CancellationToken));
        Assert.Equal(1, (await final.Accounts.SingleAsync(value => value.Id == id, TestContext.Current.CancellationToken)).SecurityVersion);
    }

    [Fact]
    public async Task CommandBrowserAndBodyDenials_DoNotRevokeOrDeleteCookies()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now));
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        Assert.True(factory.Services.GetRequiredService<AuthenticationTokens>().TryReadActor(grant.Access, out var actor));
        client.DefaultRequestHeaders.Remove("X-EduCenterOS-Auth");
        foreach (var route in new[] { "/api/v1/auth/logout", "/api/v1/auth/logout-all", RevokeRoute(actor.SessionId) })
        {
            using var marker = await CommandAsync(client, route, grant.Access, grant.Refresh, marker: false);
            await AssertRejectedAsync(marker, HttpStatusCode.Forbidden, "IdentityAccess.BrowserRequestRejected");
            using var body = await CommandAsync(client, route, grant.Access, grant.Refresh, "{\"unexpected\":true}");
            await AssertRejectedAsync(body, HttpStatusCode.BadRequest, "Validation.Failed");
            using var http = await CommandAsync(client, "http://localhost:5443" + route, grant.Access, grant.Refresh);
            await AssertRejectedAsync(http, HttpStatusCode.Forbidden, "IdentityAccess.BrowserRequestRejected");
        }
        await using var final = Context(database);
        Assert.Null((await final.Sessions.SingleAsync(TestContext.Current.CancellationToken)).RevokedAtUtc);
        Assert.Null((await final.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken)).ConsumedAtUtc);
    }

    [Fact]
    public async Task LogoutResolutionFailure_IsInfrastructureFailureAndNeverAnExternalNoOp()
    {
        await PrepareAsync(database);
        var fault = new RefreshResolutionFault();
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now), fault);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        fault.Enabled = true;
        foreach (var cookie in new[] { grant.Refresh, AuthenticationTokens.NewRefresh() })
        {
            using var failure = await CommandAsync(client, "/api/v1/auth/logout", null, cookie);
            await AssertRejectedAsync(failure, HttpStatusCode.InternalServerError, "General.UnexpectedError");
        }
        using var missing = await CommandAsync(client, "/api/v1/auth/logout", null, null);
        await AssertNoContentAsync(missing, true);
        await using var final = Context(database);
        Assert.Null((await final.Sessions.SingleAsync(TestContext.Current.CancellationToken)).RevokedAtUtc);
        Assert.DoesNotContain(factory.Logs.Events, item => item.Message.Contains("synthetic-refresh-resolution-fault", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogoutAllActualCommitFault_HasNoPartialRevocationOrFalseCookieSuccess(bool afterCommit)
    {
        await PrepareAsync(database);
        var fault = new AuthenticationCommitFault(afterCommit);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now), fault);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        var grants = new List<TestAuthenticationGrant>();
        for (var index = 0; index < 3; index++)
        {
            using var login = await LoginAsync(client);
            grants.Add(await ReadGrantAsync(login));
        }
        fault.Enabled = true;
        using var response = await CommandAsync(client, "/api/v1/auth/logout-all", grants[0].Access, grants[2].Refresh);
        await AssertRejectedAsync(response, HttpStatusCode.InternalServerError, "General.UnexpectedError");
        Assert.Equal(1, fault.CommitCalls);
        Assert.Null(response.Headers.RetryAfter);
        fault.Enabled = false;
        await using var final = Context(database);
        Assert.Equal(afterCommit ? 3 : 0, await final.Sessions.CountAsync(session => session.RevokedAtUtc != null, TestContext.Current.CancellationToken));
        Assert.Equal(1, (await final.Accounts.SingleAsync(TestContext.Current.CancellationToken)).SecurityVersion);
        using var next = await MeAsync(client, grants[0].Access);
        if (afterCommit) await AssertRejectedAsync(next);
        else Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SingleSessionCommands_WithholdSuccessAndCookieDeletionAcrossRealCommitLoss(bool revoke, bool afterCommit)
    {
        await PrepareAsync(database);
        var fault = new AuthenticationCommitFault(afterCommit);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now), fault);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        Assert.True(factory.Services.GetRequiredService<AuthenticationTokens>().TryReadActor(grant.Access, out var actor));
        fault.Enabled = true;
        using var response = await CommandAsync(client, revoke ? RevokeRoute(actor.SessionId) : "/api/v1/auth/logout", grant.Access, grant.Refresh);
        await AssertRejectedAsync(response, HttpStatusCode.InternalServerError, "General.UnexpectedError");
        Assert.Equal(1, fault.CommitCalls);
        Assert.Null(response.Headers.RetryAfter);
        fault.Enabled = false;
        await using var final = Context(database);
        var session = await final.Sessions.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(afterCommit, session.RevokedAtUtc is not null);
        if (afterCommit) Assert.Equal(revoke ? "UserRequest" : "LogoutCurrent", session.RevocationReason);
        Assert.Equal(1, (await final.Accounts.SingleAsync(TestContext.Current.CancellationToken)).SecurityVersion);
        using var next = await MeAsync(client, grant.Access);
        if (afterCommit) await AssertRejectedAsync(next);
        else Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Theory]
    [InlineData("logout", false)]
    [InlineData("logout", true)]
    [InlineData("logoutAll", false)]
    [InlineData("logoutAll", true)]
    [InlineData("revoke", false)]
    [InlineData("revoke", true)]
    public async Task RefreshAndRevocation_ObserveRealBlockingAndTerminalLinearizedOutcome(string mode, bool refreshFirst)
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
        Assert.True(factory.Services.GetRequiredService<AuthenticationTokens>().TryReadActor(original.Access, out var actor));
        var route = mode switch
        {
            "logout" => "/api/v1/auth/logout",
            "logoutAll" => "/api/v1/auth/logout-all",
            _ => RevokeRoute(actor.SessionId)
        };
        clock.UtcNow = Now.AddSeconds(20);
        await using var observer = new NpgsqlConnection(database.AdminConnectionString);
        await observer.OpenAsync(TestContext.Current.CancellationToken);
        probe.Enabled = true;
        gate.Enabled = true;
        var first = refreshFirst ? RefreshAsync(client, original.Refresh) : CommandAsync(client, route, original.Access, original.Refresh);
        Task<HttpResponseMessage>? second = null;
        HttpResponseMessage[] responses = [];
        var observedOverlap = false;
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            second = refreshFirst ? CommandAsync(client, route, original.Access, original.Refresh) : RefreshAsync(client, original.Refresh);
            var backend = await probe.SecondBackendPid.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            await AssertBlockedAsync(observer, backend);
            clock.UtcNow = Now.AddSeconds(40);
            observedOverlap = true;
        }
        finally
        {
            gate.Release.TrySetResult();
            responses = second is null ? [await first] : await Task.WhenAll(first, second);
            if (!observedOverlap)
                foreach (var response in responses) response.Dispose();
        }
        TestAuthenticationGrant? replacement = null;
        try
        {
            if (refreshFirst)
            {
                replacement = await ReadGrantAsync(responses[0]);
                await AssertNoContentAsync(responses[1], true);
            }
            else
            {
                await AssertNoContentAsync(responses[0], true);
                await AssertRejectedAsync(responses[1]);
            }
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
        Assert.Equal(2, probe.Contexts.Distinct().Count());
        using var denied = await MeAsync(client, original.Access);
        await AssertRejectedAsync(denied);
        if (replacement is not null)
        {
            using var next = await RefreshAsync(client, replacement.Refresh);
            await AssertRejectedAsync(next);
        }
        await using var final = Context(database);
        var session = await final.Sessions.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(refreshFirst ? Now.AddSeconds(40) : Now.AddSeconds(20), session.RevokedAtUtc);
        Assert.Equal(mode switch { "logout" => "LogoutCurrent", "logoutAll" => "LogoutAll", _ => "UserRequest" }, session.RevocationReason);
        Assert.Equal(refreshFirst ? 2 : 1, await final.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(refreshFirst ? 1 : 0, await final.RefreshTokens.CountAsync(credential => credential.ConsumedAtUtc != null, TestContext.Current.CancellationToken));
        Assert.Equal(1, (await final.Accounts.SingleAsync(TestContext.Current.CancellationToken)).SecurityVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginAndLogoutAll_ObserveBoundaryAndAllowOnlyLaterCreatedSession(bool logoutFirst)
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        var gate = new AuthenticationCommitGate();
        var probe = new AuthenticationAccountLockProbe { Enabled = false };
        await using var factory = new TestingApiFactory(database, clock, gate, configureServices: services => services.AddSingleton<IInterceptor>(probe));
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        using var initial = await LoginAsync(client);
        var original = await ReadGrantAsync(initial);
        clock.UtcNow = Now.AddSeconds(20);
        await using var observer = new NpgsqlConnection(database.AdminConnectionString);
        await observer.OpenAsync(TestContext.Current.CancellationToken);
        probe.Enabled = true;
        gate.Enabled = true;
        var first = logoutFirst ? CommandAsync(client, "/api/v1/auth/logout-all", original.Access, original.Refresh) : LoginAsync(client);
        Task<HttpResponseMessage>? second = null;
        HttpResponseMessage[] responses = [];
        var observedOverlap = false;
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            second = logoutFirst ? LoginAsync(client) : CommandAsync(client, "/api/v1/auth/logout-all", original.Access, original.Refresh);
            var backend = await probe.SecondBackendPid.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            await AssertBlockedAsync(observer, backend);
            clock.UtcNow = Now.AddSeconds(40);
            observedOverlap = true;
        }
        finally
        {
            gate.Release.TrySetResult();
            responses = second is null ? [await first] : await Task.WhenAll(first, second);
            if (!observedOverlap)
                foreach (var response in responses) response.Dispose();
        }
        TestAuthenticationGrant? created = null;
        try
        {
            await AssertNoContentAsync(responses[logoutFirst ? 0 : 1], true);
            created = await ReadGrantAsync(responses[logoutFirst ? 1 : 0]);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
        Assert.Equal(2, probe.Contexts.Distinct().Count());
        Assert.NotNull(created);
        using var previous = await MeAsync(client, original.Access);
        await AssertRejectedAsync(previous);
        using var latest = await MeAsync(client, created.Access);
        if (logoutFirst) Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        else await AssertRejectedAsync(latest);
        await using var final = Context(database);
        Assert.Equal(2, await final.Sessions.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(logoutFirst ? 1 : 0, await final.Sessions.CountAsync(session => session.RevokedAtUtc == null, TestContext.Current.CancellationToken));
        Assert.Equal(1, (await final.Accounts.SingleAsync(TestContext.Current.CancellationToken)).SecurityVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedCommand_RechecksActorDeadlineAfterWaitingForOwnerClaim(bool revoke)
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        var probe = new AuthenticationAccountLockProbe { Enabled = false };
        await using var factory = new TestingApiFactory(database, clock, probe, authenticationPolicy: policy =>
        {
            policy["accessLifetimeSeconds"] = 600;
            policy["idleTimeoutSeconds"] = 600;
            policy["absoluteLifetimeSeconds"] = 900;
        });
        using var client = Browser(factory);
        var id = await SeedAsync(database, factory);
        using var login = await LoginAsync(client);
        var grant = await ReadGrantAsync(login);
        Assert.True(factory.Services.GetRequiredService<AuthenticationTokens>().TryReadActor(grant.Access, out var actor));
        clock.UtcNow = Now.AddSeconds(599);
        await using var held = Context(database);
        await using var transaction = await held.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await AuthenticationTransactions.LockAccountAsync(held, id, TestContext.Current.CancellationToken);
        await using var observer = new NpgsqlConnection(database.AdminConnectionString);
        await observer.OpenAsync(TestContext.Current.CancellationToken);
        probe.Enabled = true;
        var pending = CommandAsync(client, revoke ? RevokeRoute(actor.SessionId) : "/api/v1/auth/logout-all", grant.Access, grant.Refresh);
        HttpResponseMessage? response = null;
        var observedOverlap = false;
        try
        {
            var backend = await probe.FirstBackendPid.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            await AssertBlockedAsync(observer, backend);
            clock.UtcNow = Now.AddSeconds(600);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
            observedOverlap = true;
        }
        finally
        {
            await transaction.DisposeAsync();
            response = await pending;
            if (!observedOverlap) response.Dispose();
        }
        using (response) await AssertRejectedAsync(response);
        await using var final = Context(database);
        Assert.Null((await final.Sessions.SingleAsync(TestContext.Current.CancellationToken)).RevokedAtUtc);
    }

    private static string RevokeRoute(Guid id) => "/api/v1/auth/sessions/" + id.ToString("D") + "/revoke";

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string access, string route) => MeAsync(client, access, route);

    internal static async Task AssertNoContentAsync(HttpResponseMessage response, bool cookieDeleted)
    {
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        if (!cookieDeleted)
        {
            Assert.False(response.Headers.Contains("Set-Cookie"));

            return;
        }
        var cookie = SetCookieHeaderValue.Parse(response.Headers.GetValues("Set-Cookie").Single());
        Assert.Equal("__Secure-educenteros-refresh", cookie.Name.Value);
        Assert.Equal("/api/v1/auth", cookie.Path.Value);
        Assert.True(cookie.Secure && cookie.HttpOnly);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.False(cookie.Domain.HasValue);
        Assert.True(cookie.Expires < Now);
        Assert.True(cookie.MaxAge is null || cookie.MaxAge <= TimeSpan.Zero);
        Assert.True(!cookie.Value.HasValue || cookie.Value.Value == "");
    }

    private static async Task AssertBlockedAsync(NpgsqlConnection observer, int backend)
    {
        var blocked = false;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (!blocked && DateTimeOffset.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid))", observer) { CommandTimeout = 5 };
            command.Parameters.AddWithValue("pid", backend);
            blocked = (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))! > 0;
        }
        Assert.True(blocked, "The independent operation must actually wait on the authoritative PostgreSQL owner claim.");
    }
}

internal sealed class RefreshResolutionFault : DbCommandInterceptor
{
    internal bool Enabled { get; set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (
            Enabled &&
            command.CommandText.Contains("refresh_token_records", StringComparison.Ordinal) &&
            command.CommandText.Contains("JOIN", StringComparison.Ordinal)
        )
            throw new IOException("synthetic-refresh-resolution-fault");

        return ValueTask.FromResult(result);
    }
}
