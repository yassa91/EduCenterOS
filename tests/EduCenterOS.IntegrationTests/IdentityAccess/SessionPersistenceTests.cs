using System.Security.Cryptography;
using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class SessionPersistenceTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 8, 0, 0, TimeSpan.Zero);

    private IdentityAccessDbContext Context() => new(IdentityAccessDbContext.Options(database.ModuleConnectionString));

    private async Task<Guid> PrepareAccount()
    {
        await database.PrepareIdentityAsync();
        await database.ResetIdentityAsync();
        await using var context = Context();
        var person = new PersonIdentity(Guid.CreateVersion7(), "Session Test", Now);
        var account = new UserAccount(Guid.CreateVersion7(), person.Id, "+201012345678", null, "synthetic-hash", Now, Now);
        context.People.Add(person);
        context.Accounts.Add(account);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return account.Id;
    }

    [Fact]
    public async Task RefreshLineageRoundTripsWithoutRawCredentials()
    {
        var account = await PrepareAccount();
        await using var context = Context();
        var session = new UserSession(Guid.CreateVersion7(), account, 1, Now, 60, 120);
        var first = new RefreshTokenRecord(Guid.CreateVersion7(), session.Id, RandomNumberGenerator.GetBytes(32), Now, Now.AddSeconds(60));
        var second = new RefreshTokenRecord(Guid.CreateVersion7(), session.Id, RandomNumberGenerator.GetBytes(32), Now.AddSeconds(1), Now.AddSeconds(61));
        first.Consume(Now.AddSeconds(1), second.Id);
        session.RefreshActivity(Now.AddSeconds(1), 60);
        context.Sessions.Add(session);
        context.RefreshTokens.AddRange(first, second);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var fresh = Context();
        var loaded = await fresh.RefreshTokens.SingleAsync(value => value.Id == first.Id, TestContext.Current.CancellationToken);
        Assert.Equal(second.Id, loaded.ReplacedByTokenId);
        Assert.Equal(32, loaded.TokenHash.Length);
        Assert.False(loaded.CanConsume(Now.AddSeconds(2)));
        Assert.Equal(Now.AddSeconds(61), (await fresh.Sessions.SingleAsync(TestContext.Current.CancellationToken)).IdleExpiresAtUtc);
    }

    [Fact]
    public async Task DatabaseRejectsDuplicateRefreshHashes()
    {
        var account = await PrepareAccount();
        await using var context = Context();
        var session = new UserSession(Guid.CreateVersion7(), account, 1, Now, 60, 120);
        var hash = RandomNumberGenerator.GetBytes(32);
        context.Sessions.Add(session);
        context.RefreshTokens.Add(new RefreshTokenRecord(Guid.CreateVersion7(), session.Id, hash, Now, Now.AddSeconds(60)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.RefreshTokens.Add(new RefreshTokenRecord(Guid.CreateVersion7(), session.Id, hash, Now, Now.AddSeconds(60)));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [Fact]
    public async Task ReplacementCannotBelongToAnotherSession()
    {
        var account = await PrepareAccount();
        await using var context = Context();
        var firstSession = new UserSession(Guid.CreateVersion7(), account, 1, Now, 60, 120);
        var secondSession = new UserSession(Guid.CreateVersion7(), account, 1, Now, 60, 120);
        var first = new RefreshTokenRecord(Guid.CreateVersion7(), firstSession.Id, RandomNumberGenerator.GetBytes(32), Now, Now.AddSeconds(60));
        var second = new RefreshTokenRecord(Guid.CreateVersion7(), secondSession.Id, RandomNumberGenerator.GetBytes(32), Now, Now.AddSeconds(60));
        context.Sessions.AddRange(firstSession, secondSession);
        context.RefreshTokens.AddRange(first, second);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        first.Consume(Now.AddSeconds(1), second.Id);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [Fact]
    public async Task SessionCreationRollsBackTogetherWithRefreshRecord()
    {
        var account = await PrepareAccount();
        await using var context = Context();
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var session = new UserSession(Guid.CreateVersion7(), account, 1, Now, 60, 120);
            context.Sessions.Add(session);
            context.RefreshTokens.Add(new RefreshTokenRecord(Guid.CreateVersion7(), session.Id, RandomNumberGenerator.GetBytes(32), Now, Now.AddSeconds(60)));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        await using var fresh = Context();
        Assert.Equal(0, await fresh.Sessions.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await fresh.RefreshTokens.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await fresh.Accounts.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentAccountFailuresPreserveBothIncrementsUnderRowLock()
    {
        var account = await PrepareAccount();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        await using var first = Context();
        await using var second = Context();
        await second.Database.OpenConnectionAsync(deadline.Token);
        var secondPid = await second.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync(deadline.Token);
        await using var observer = new NpgsqlConnection(database.AdminConnectionString);
        await observer.OpenAsync(deadline.Token);
        await using var firstTransaction = await first.Database.BeginTransactionAsync(deadline.Token);
        var firstAccount = await first.Accounts.FromSqlInterpolated(
            $"SELECT * FROM identity_access.user_accounts WHERE id={account} FOR UPDATE"
        ).SingleAsync(deadline.Token);

        async Task CompetingFailure()
        {
            await using var transaction = await second.Database.BeginTransactionAsync(deadline.Token);
            var secondAccount = await second.Accounts.FromSqlInterpolated(
                $"SELECT * FROM identity_access.user_accounts WHERE id={account} FOR UPDATE"
            ).SingleAsync(deadline.Token);
            secondAccount.FailAuthentication(Now.AddSeconds(1), 5, 300);
            await second.SaveChangesAsync(deadline.Token);
            await transaction.CommitAsync(deadline.Token);
        }

        var competing = CompetingFailure();

        try
        {
            var blocked = false;

            while (!deadline.IsCancellationRequested)
            {
                await using var command = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid))", observer);
                command.Parameters.AddWithValue("pid", secondPid);

                if (Convert.ToInt32(await command.ExecuteScalarAsync(deadline.Token)) > 0)
                {
                    blocked = true;

                    break;
                }
            }

            Assert.True(blocked, "The second independent connection must contend on the account lock.");
            firstAccount.FailAuthentication(Now, 5, 300);
            await first.SaveChangesAsync(deadline.Token);
            await firstTransaction.CommitAsync(deadline.Token);
            await competing.WaitAsync(deadline.Token);
        }
        finally
        {
            await firstTransaction.DisposeAsync();

            if (!competing.IsCompleted) deadline.Cancel();

            try
            {
                await competing;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
            }
        }

        await using var fresh = Context();
        Assert.Equal(2, (await fresh.Accounts.SingleAsync(TestContext.Current.CancellationToken)).AccessFailedCount);
    }

    [Fact]
    public async Task RevocationConstraintRejectsMissingReason()
    {
        var account = await PrepareAccount();
        await using var context = Context();
        var session = new UserSession(Guid.CreateVersion7(), account, 1, Now, 60, 120);
        context.Sessions.Add(session);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE identity_access.user_sessions SET revoked_at_utc={Now.AddSeconds(1)} WHERE id={session.Id}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
    }
}
