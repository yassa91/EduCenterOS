using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Microsoft.Extensions.Hosting;
using EduCenterOS.IntegrationTests.Api;
using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static EduCenterOS.IntegrationTests.IdentityAccess.AuthenticationTestSupport;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class LoginTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    [Fact]
    public async Task RegisteredAccount_LoginCreatesOnlyHashedRefreshAndValidatedAccessAfterCommit()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now));
        using var client = Browser(factory);
        using var issue = await client.PostAsJsonAsync("/api/v1/phone-verifications", new { phoneNumber = "01012345678" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
        using var issueBody = JsonDocument.Parse(await issue.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var challengeId = issueBody.RootElement.GetProperty("challengeId").GetGuid();
        using var verify = await client.PostAsJsonAsync($"/api/v1/phone-verifications/{challengeId}/verify", new { code = factory.Sender.Read(challengeId).Code }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        using var proof = JsonDocument.Parse(await verify.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var registration = await client.PostAsJsonAsync("/api/v1/accounts", new { challengeId, verificationProof = proof.RootElement.GetProperty("verificationProof").GetString(), fullName = "مستخدم الاختبار", password = Password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.False(registration.Headers.Contains("Set-Cookie"));
        using var response = await LoginAsync(client, " 00201012345678 ");
        var grant = await ReadGrantAsync(response);
        var tokens = factory.Services.GetRequiredService<AuthenticationTokens>();
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ValidateToken(grant.Access, tokens.ValidationParameters(), out _);
        Assert.True(tokens.TryReadActor(grant.Access, out var actor));
        await using var context = Context(database);
        var account = await context.Accounts.SingleAsync(TestContext.Current.CancellationToken);
        var session = await context.Sessions.SingleAsync(TestContext.Current.CancellationToken);
        var credential = await context.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(account.Id, session.UserAccountId);
        Assert.Equal(account.Id, actor.AccountId);
        Assert.Equal(session.Id, actor.SessionId);
        Assert.Equal(account.SecurityVersion, session.SecurityVersionAtAuthentication);
        Assert.Equal(Now.AddSeconds(600), grant.ExpiresAtUtc);
        Assert.Equal(Now.AddDays(30), credential.ExpiresAtUtc);
        Assert.True(CryptographicOperations.FixedTimeEquals(AuthenticationTokens.HashRefresh(grant.Refresh), credential.TokenHash), "Only the expected digest may persist.");
        Assert.Equal(0, account.AccessFailedCount);
        Assert.Null(account.LockoutEndUtc);
        Assert.Null(credential.ConsumedAtUtc);
        Assert.Single((await context.LoginTargets.SingleAsync(TestContext.Current.CancellationToken)).AttemptsUtc);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        foreach (var secret in new[] { grant.Refresh, Password, account.PasswordHash, factory.Services.GetRequiredService<AuthenticationRuntimeSettings>().PrivateKeyPem })
        {
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            Assert.DoesNotContain(factory.Logs.Events, entry => entry.Message.Contains(secret, StringComparison.Ordinal));
        }
        Assert.DoesNotContain(factory.Logs.Events, entry => entry.Message.Contains(grant.Access, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lockout_ExactDeadlineResetsFailuresAndCorrectPasswordWithoutExtendingLockout()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        await using var factory = new TestingApiFactory(database, clock);
        using var client = Browser(factory);
        var id = await SeedAsync(database, factory);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var rejected = await LoginAsync(client, password: "wrong-synthetic-password");
            await AssertRejectedAsync(rejected);
        }
        clock.UtcNow = Now.AddSeconds(299);
        using var locked = await LoginAsync(client);
        await AssertRejectedAsync(locked);
        await using (var check = Context(database))
        {
            var account = await check.Accounts.SingleAsync(value => value.Id == id, TestContext.Current.CancellationToken);
            Assert.Equal(5, account.AccessFailedCount);
            Assert.Equal(Now.AddSeconds(300), account.LockoutEndUtc);
            Assert.Empty(await check.Sessions.ToListAsync(TestContext.Current.CancellationToken));
        }
        clock.UtcNow = Now.AddSeconds(300);
        using var accepted = await LoginAsync(client);
        await ReadGrantAsync(accepted);
        await using var final = Context(database);
        var reset = await final.Accounts.SingleAsync(value => value.Id == id, TestContext.Current.CancellationToken);
        Assert.Equal(0, reset.AccessFailedCount);
        Assert.Null(reset.LockoutEndUtc);
        Assert.Single(await final.Sessions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HistoricalShortPassword_RehashPreservesSecurityEpochAndPasswordChangedTime()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now));
        using var client = Browser(factory);
        var id = await SeedAsync(database, factory, password: " Old8 ", legacyIterations: 10000);
        string oldHash;
        await using (var before = Context(database)) oldHash = (await before.Accounts.SingleAsync(TestContext.Current.CancellationToken)).PasswordHash;
        using var response = await LoginAsync(client, password: " Old8 ");
        await ReadGrantAsync(response);
        await using var after = Context(database);
        var account = await after.Accounts.SingleAsync(value => value.Id == id, TestContext.Current.CancellationToken);
        Assert.True(oldHash != account.PasswordHash, "The historical password hash must be upgraded.");
        Assert.Equal(1, account.SecurityVersion);
        Assert.Equal(Now, account.PasswordChangedAtUtc);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(PasswordVerificationResult.Success, scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserAccount>>().VerifyHashedPassword(account, account.PasswordHash, " Old8 "));
    }

    [Fact]
    public async Task UnknownClosedSuspendedAndNotYetVerified_ReturnSamePublicFailure()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now));
        using var client = Browser(factory);
        var suspended = await SeedAsync(database, factory);
        var closed = await SeedAsync(database, factory, "01112345678");
        await SeedAsync(database, factory, "01212345678", createdAt: Now.AddSeconds(1));
        await using (var setup = Context(database))
        {
            await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_accounts SET status=1,version=version+1 WHERE id={suspended}", TestContext.Current.CancellationToken);
            await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_accounts SET status=2,version=version+1 WHERE id={closed}", TestContext.Current.CancellationToken);
        }
        string? detail = null;
        foreach (var phone in new[] { "01012345678", "01112345678", "01212345678", "01512345678" })
        {
            using var response = await LoginAsync(client, phone);
            await AssertRejectedAsync(response);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            detail ??= body.RootElement.GetProperty("detail").GetString();
            Assert.Equal(detail, body.RootElement.GetProperty("detail").GetString());
            Assert.Null(response.Headers.RetryAfter);
        }
        await using var final = Context(database);
        Assert.Empty(await final.Sessions.ToListAsync(TestContext.Current.CancellationToken));
        Assert.All(await final.Accounts.ToListAsync(TestContext.Current.CancellationToken), account => Assert.Equal(0, account.AccessFailedCount));
        Assert.Equal(4, await final.LoginTargets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IdentifierBudget_IncludesUnknownAccountsAndSurvivesHostRestart()
    {
        await PrepareAsync(database);
        var clock = new ControlledClock(Now);
        await using (var factory = new TestingApiFactory(database, clock))
        {
            using var client = Browser(factory);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                using var response = await LoginAsync(client, "01512345678");
                await AssertRejectedAsync(response);
            }
        }
        await using var restarted = new TestingApiFactory(database, clock);
        using var next = Browser(restarted);
        using var denied = await LoginAsync(next, "01512345678");
        await AssertRejectedAsync(denied, HttpStatusCode.TooManyRequests, "IdentityAccess.AuthenticationThrottled");
        Assert.Equal(TimeSpan.FromSeconds(900), denied.Headers.RetryAfter?.Delta);
        clock.UtcNow = Now.AddSeconds(900);
        using var expired = await LoginAsync(next, "01512345678");
        await AssertRejectedAsync(expired);
        await using var final = Context(database);
        Assert.Single((await final.LoginTargets.SingleAsync(TestContext.Current.CancellationToken)).AttemptsUtc);
    }

    [Fact]
    public async Task SourceBudget_DoesNotTrustForwardedAddressesOrExecuteRejectedAttempt()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now), authenticationPolicy: policy => policy["sourcePermits"] = 1);
        using var client = Browser(factory);
        using var first = await LoginAsync(client);
        await AssertRejectedAsync(first);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.123");
        using var second = await LoginAsync(client, "01112345678");
        await AssertRejectedAsync(second, HttpStatusCode.TooManyRequests, "IdentityAccess.AuthenticationThrottled");
        await using var final = Context(database);
        Assert.Equal(1, await final.LoginTargets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualCommitFault_DoesNotReturnCredentialsOrReplayAndPreservesRealOutcome(bool afterCommit)
    {
        await PrepareAsync(database);
        var fault = new AuthenticationCommitFault(afterCommit);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now), fault);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        fault.Enabled = true;
        using var response = await LoginAsync(client);
        await AssertRejectedAsync(response, HttpStatusCode.InternalServerError, "General.UnexpectedError");
        Assert.Null(response.Headers.RetryAfter);
        Assert.Equal(1, fault.CommitCalls);
        await using var final = Context(database);
        Assert.Equal(afterCommit ? 1 : 0, await final.Sessions.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(afterCommit ? 1 : 0, await final.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(factory.Logs.Events, entry => entry.Message.Contains("synthetic-auth-commit-fault", StringComparison.Ordinal));
    }
    [Fact]
    public async Task LoginTransportAndBrowserDenials_RejectBeforePersistentEffects()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now));
        using var client = Browser(factory);
        var valid = JsonSerializer.Serialize(new { phoneNumber = "01012345678", password = Password });
        foreach (var shape in new[] { "{}", "[]", "null", "{\"phoneNumber\":1,\"password\":\"synthetic\"}", valid[..^1] + ",\"role\":\"Owner\"}", valid[..^1] + ",\"Password\":\"synthetic\"}", JsonSerializer.Serialize(new { phoneNumber = "invalid", password = "" }), JsonSerializer.Serialize(new { phoneNumber = "01012345678", password = new string('x', 129) }) })
        {
            using var rejected = await client.PostAsync("/api/v1/auth/login", new StringContent(shape, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
            await AssertRejectedAsync(rejected, HttpStatusCode.BadRequest, "Validation.Failed");
        }
        client.DefaultRequestHeaders.Clear();
        foreach (var fault in new[] { "http", "missingOrigin", "foreignOrigin", "duplicateOrigin", "missingMarker", "wrongMarker", "duplicateMarker" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, fault == "http" ? "http://localhost:5443/api/v1/auth/login" : "/api/v1/auth/login");
            request.Content = new StringContent(valid, Encoding.UTF8, "application/json");
            if (fault != "missingOrigin") request.Headers.TryAddWithoutValidation("Origin", fault == "foreignOrigin" ? "https://other.invalid" : TestingApiFactory.AuthenticationOrigin);
            if (fault == "duplicateOrigin") request.Headers.TryAddWithoutValidation("Origin", TestingApiFactory.AuthenticationOrigin);
            if (fault != "missingMarker") request.Headers.Add("X-EduCenterOS-Auth", fault == "wrongMarker" ? "true" : "1");
            if (fault == "duplicateMarker") request.Headers.Add("X-EduCenterOS-Auth", "1");
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            await AssertRejectedAsync(response, HttpStatusCode.Forbidden, "IdentityAccess.BrowserRequestRejected");
        }
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        preflight.Headers.Add("Origin", TestingApiFactory.AuthenticationOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-educenteros-auth");
        using var cors = await client.SendAsync(preflight, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, cors.StatusCode);
        Assert.Equal(TestingApiFactory.AuthenticationOrigin, cors.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", cors.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        await using var final = Context(database);
        Assert.Empty(await final.LoginTargets.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await final.Sessions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommitBoundary_WithholdsResponseUntilNewSessionIsAuthoritativelyVisible()
    {
        await PrepareAsync(database);
        var gate = new AuthenticationCommitGate();
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now), gate);
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        gate.Enabled = true;
        var pending = LoginAsync(client);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.False(pending.IsCompleted, "Credentials must not arrive before Commit.");
            await using var beforeCommit = Context(database);
            Assert.Equal(0, await beforeCommit.Sessions.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await beforeCommit.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        using var response = await pending;
        await ReadGrantAsync(response);
        await using var committed = Context(database);
        Assert.Equal(1, await committed.Sessions.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await committed.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChangedSecurityEpochAfterRealPasswordVerification_RejectsStaleEvidence()
    {
        await PrepareAsync(database);
        PasswordVerificationGate? gate = null;
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now), configureServices: services =>
        {
            services.RemoveAll<IPasswordHasher<UserAccount>>();
            services.AddScoped<IPasswordHasher<UserAccount>>(provider =>
            {
                gate ??= new PasswordVerificationGate(new PasswordHasher<UserAccount>(provider.GetRequiredService<IOptions<PasswordHasherOptions>>()));

                return gate;
            });
        });
        using var client = Browser(factory);
        var id = await SeedAsync(database, factory);
        Assert.NotNull(gate);
        gate.Enabled = true;
        var pending = LoginAsync(client);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            await using var changed = Context(database);
            await using var transaction = await changed.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await AuthenticationTransactions.LockAccountAsync(changed, id, TestContext.Current.CancellationToken);
            await changed.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_accounts SET security_version=security_version+1,version=version+1 WHERE id={id}", TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        using var response = await pending;
        await AssertRejectedAsync(response);
        await using var final = Context(database);
        Assert.Empty(await final.Sessions.ToListAsync(TestContext.Current.CancellationToken));
        var account = await final.Accounts.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, account.SecurityVersion);
        Assert.Equal(0, account.AccessFailedCount);
    }

    [Fact]
    public async Task ConcurrentFailedLogins_ObserveRealAccountRowBlockingWithoutLosingAttempts()
    {
        await PrepareAsync(database);
        var gate = new AuthenticationAccountCommitGate();
        var probe = new AuthenticationAccountLockProbe();
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now), gate,
            configureServices: services => services.AddSingleton<IInterceptor>(probe));
        using var client = Browser(factory);
        await SeedAsync(database, factory);
        await using var observer = new NpgsqlConnection(database.AdminConnectionString);
        await observer.OpenAsync(TestContext.Current.CancellationToken);
        var first = LoginAsync(client, password: "wrong-synthetic-password");
        Task<HttpResponseMessage>? second = null;
        HttpResponseMessage[] responses = [];
        var observedOverlap = false;
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            second = LoginAsync(client, password: "wrong-synthetic-password");
            var backend = await probe.SecondBackendPid.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            var blocked = false;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (!blocked && DateTimeOffset.UtcNow < deadline)
            {
                await using var query = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid))", observer) { CommandTimeout = 5 };
                query.Parameters.AddWithValue("pid", backend);
                blocked = (int)(await query.ExecuteScalarAsync(TestContext.Current.CancellationToken))! > 0;
            }
            Assert.True(blocked, "The second login must contend on the authoritative account row.");
            observedOverlap = true;
        }
        finally
        {
            gate.Release.TrySetResult();
            responses = second is null ? [await first] : await Task.WhenAll(first, second);

            if (!observedOverlap)
                foreach (var response in responses) response.Dispose();
        }
        Assert.NotNull(second);
        try
        {
            foreach (var response in responses) await AssertRejectedAsync(response);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
        Assert.Equal(2, probe.Contexts.Distinct().Count());
        await using var final = Context(database);
        Assert.Equal(2, (await final.Accounts.SingleAsync(TestContext.Current.CancellationToken)).AccessFailedCount);
        Assert.Equal(2, (await final.LoginTargets.SingleAsync(TestContext.Current.CancellationToken)).AttemptsUtc.Length);
        Assert.Empty(await final.Sessions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BudgetCleanup_SkipsLockedTargetAndPreservesActiveAttempts()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now));
        using var client = Browser(factory);
        var digest = factory.Services.GetRequiredService<RegistrationCryptography>().LoginDigest("+201012345678");
        await using (var setup = Context(database))
        {
            var old = new LoginTarget(digest);
            old.Reserve(Now.AddHours(-25), 900, 10);
            setup.LoginTargets.Add(old);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using var held = Context(database);
        await using var transaction = await held.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var target = await held.LoginTargets.FromSqlInterpolated($"SELECT * FROM identity_access.login_targets WHERE digest={digest} FOR UPDATE").SingleAsync(TestContext.Current.CancellationToken);
        target.Reserve(Now, 900, 10);
        await held.SaveChangesAsync(TestContext.Current.CancellationToken);
        var cleanup = factory.Services.GetServices<IHostedService>().OfType<AuthenticationBudgetCleanup>().Single();
        await cleanup.SweepAsync(TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        await cleanup.SweepAsync(TestContext.Current.CancellationToken);
        await using var final = Context(database);
        var surviving = await final.LoginTargets.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Now, surviving.LastAttemptAtUtc);
        Assert.Single(surviving.AttemptsUtc);
        Assert.Equal(Now, surviving.AttemptsUtc[0]);
    }

    [Fact]
    public async Task SigningFailure_RollsBackAccountResetAndNeverReturnsCredentials()
    {
        await PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(Now));
        using var client = Browser(factory);
        var id = await SeedAsync(database, factory);
        await using (var setup = Context(database))
            await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_access.user_accounts SET access_failed_count=2,version=version+1 WHERE id={id}", TestContext.Current.CancellationToken);
        factory.Services.GetRequiredService<AuthenticationTokens>().Dispose();
        using var response = await LoginAsync(client);
        await AssertRejectedAsync(response, HttpStatusCode.InternalServerError, "General.UnexpectedError");
        await using var final = Context(database);
        Assert.Equal(2, (await final.Accounts.SingleAsync(TestContext.Current.CancellationToken)).AccessFailedCount);
        Assert.Empty(await final.Sessions.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await final.RefreshTokens.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single((await final.LoginTargets.SingleAsync(TestContext.Current.CancellationToken)).AttemptsUtc);
    }

}
