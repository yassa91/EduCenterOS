using System.Data.Common;
using EduCenterOS.IntegrationTests.Api;
using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Features.Shared.PhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class QueryProjectionTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    [Fact]
    public async Task ChallengeTargetLookup_ReadsOneColumnInOneQueryWithoutTracking()
    {
        await AuthenticationTestSupport.PrepareAsync(database);
        var id = Guid.NewGuid();
        var now = AuthenticationTestSupport.Now;
        await using (var seed = AuthenticationTestSupport.Context(database))
        {
            seed.Challenges.Add(new OtpChallenge(id, "+201012345678", new byte[32], "v1", now, 300));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var capture = new QueryCapture();
        await using var context = Context(capture);
        var target = await ChallengeQueries.TargetFor(context, id).SingleOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.Equal("+201012345678", target);
        Assert.Empty(context.ChangeTracker.Entries());
        var query = Assert.Single(capture.Reads);
        Assert.Equal("normalized_target", Assert.Single(query.Columns));
        Assert.DoesNotContain("code_hash", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("proof_hash", query.Sql, StringComparison.Ordinal);

        Assert.Null(await ChallengeQueries.TargetFor(context, Guid.NewGuid()).SingleOrDefaultAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChallengeDeliveryCheck_UsesOneBooleanQueryAndRejectsInvalidatedState()
    {
        await AuthenticationTestSupport.PrepareAsync(database);
        var id = Guid.NewGuid();
        await using var context = Context(new QueryCapture());
        var challenge = new OtpChallenge(id, "+201012345678", new byte[32], "v1", AuthenticationTestSupport.Now, 300);
        context.Challenges.Add(challenge);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var capture = new QueryCapture();
        await using var check = Context(capture);
        Assert.True(await ChallengeQueries.Active(check, id).AnyAsync(TestContext.Current.CancellationToken));
        var query = Assert.Single(capture.Reads);
        Assert.Single(query.Columns);
        Assert.Contains("EXISTS", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("code_hash", query.Sql, StringComparison.Ordinal);
        Assert.Empty(check.ChangeTracker.Entries());

        challenge.Invalidate();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.False(await ChallengeQueries.Active(check, id).AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await ChallengeQueries.Active(check, Guid.NewGuid()).AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SessionAuthorization_ReadsOnlyLiveSecurityFactsInOneQuery()
    {
        await AuthenticationTestSupport.PrepareAsync(database);
        await using var factory = new TestingApiFactory(database, new ControlledClock(AuthenticationTestSupport.Now));
        var accountId = await AuthenticationTestSupport.SeedAsync(database, factory);
        var session = new UserSession(Guid.NewGuid(), accountId, 1, AuthenticationTestSupport.Now, 60, 90);
        await using (var seed = AuthenticationTestSupport.Context(database))
        {
            seed.Sessions.Add(session);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var capture = new QueryCapture();
        await using var context = Context(capture);
        var state = await SessionAccessQueries.ForSession(context, session.Id).SingleOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(state);
        Assert.True(state.AcceptsActor(new AuthenticatedActor(accountId, session.Id, 1), AuthenticationTestSupport.Now));
        Assert.False(state.OwnsLiveSession(AuthenticationTestSupport.Now.AddSeconds(60)));
        Assert.Empty(context.ChangeTracker.Entries());
        var query = Assert.Single(capture.Reads);
        Assert.Equal(12, query.Columns.Length);
        foreach (var forbidden in new[] { "password_hash", "token_hash", "phone_number", "email_address", "full_name" })
            Assert.DoesNotContain(forbidden, query.Sql, StringComparison.Ordinal);

        Assert.Null(await SessionAccessQueries.ForSession(context, Guid.NewGuid()).SingleOrDefaultAsync(TestContext.Current.CancellationToken));
    }

    private IdentityAccessDbContext Context(QueryCapture capture) => new(
        new DbContextOptionsBuilder<IdentityAccessDbContext>(IdentityAccessDbContext.Options(database.ModuleConnectionString))
            .AddInterceptors(capture)
            .Options
    );

    private sealed class QueryCapture : DbCommandInterceptor
    {
        internal List<(string Sql, string[] Columns)> Reads { get; } = [];

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            // Capture shape, never parameter values or credential-bearing data.
            Reads.Add((command.CommandText, Enumerable.Range(0, result.FieldCount).Select(result.GetName).ToArray()));

            return ValueTask.FromResult(result);
        }
    }
}
