using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class RegistrationPersistenceTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private IdentityAccessDbContext Runtime() => new(IdentityAccessDbContext.Options(database.ModuleConnectionString));
    private async Task Prepare()
    {
        await database.PrepareIdentityAsync(); await database.ResetIdentityAsync();
    }
    private static void AddAccount(IdentityAccessDbContext context, Guid personId, string phone, string? email = null, bool newPerson = true)
    {
        if (newPerson) context.People.Add(new PersonIdentity(personId, "Test Person", Now));
        context.Accounts.Add(new UserAccount(Guid.CreateVersion7(), personId, phone, email, "synthetic-hash", Now, Now));
    }
    [Fact]
    public async Task UpgradeFromInitialMigration_PreservesExistingAccountsAndBudgets()
    {
        await database.ResetMigrationSchemaAsync();
        var owned = database;
        await using var connection = new NpgsqlConnection(owned.AdminConnectionString); await connection.OpenAsync(TestContext.Current.CancellationToken);
        await owned.MigrateIdentityAsync(connection, "20261001150837_InitialRegistration");
        var personId = Guid.CreateVersion7(); var digest = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        await using (var initial = new IdentityAccessDbContext(IdentityAccessDbContext.Options(owned.AdminConnectionString)))
        {
            AddAccount(initial, personId, "+201012345678"); var target = new VerificationTarget(digest); target.ReserveIssue(Now,900,3,60);
            initial.Targets.Add(target); await initial.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await owned.MigrateIdentityAsync(connection);
        await using var upgraded = new IdentityAccessDbContext(IdentityAccessDbContext.Options(owned.ModuleConnectionString));
        Assert.Equal(personId, (await upgraded.Accounts.SingleAsync(TestContext.Current.CancellationToken)).PersonIdentityId);
        Assert.Equal(Now, Assert.Single((await upgraded.Targets.SingleAsync(TestContext.Current.CancellationToken)).IssuesUtc));
        Assert.Equal(3, (await upgraded.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Count());
        Assert.False(upgraded.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task FreshMigrations_CreateOwnedHistoryAndNoPendingModelChanges()
    {
        await Prepare(); await using var context = Runtime();
        Assert.Equal(3, (await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Count());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.False(context.Database.HasPendingModelChanges());
        await using var connection = new NpgsqlConnection(database.AdminConnectionString); await connection.OpenAsync(TestContext.Current.CancellationToken);
        await database.EnsureOwnedAsync(connection);
        await using var command = new NpgsqlCommand("SELECT count(*)::integer FROM pg_tables WHERE schemaname='identity_access'", connection);
        Assert.Equal(6, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
    [Theory]
    [InlineData(1,false,false)] [InlineData(100,true,true)] [InlineData(101,true,false)]
    public async Task PersonNameConstraint_UsesUtf16LengthIncludingSupplementaryCharacters(int count,bool supplementary,bool allowed)
    {
        await Prepare();await using var connection=new NpgsqlConnection(database.ModuleConnectionString);await connection.OpenAsync(TestContext.Current.CancellationToken);
        var name=string.Concat(Enumerable.Repeat(supplementary?"😀":"x",count));
        await using var command=new NpgsqlCommand("INSERT INTO identity_access.person_identities(id,full_name,created_at_utc) VALUES (@id,@name,@now)",connection);
        command.Parameters.AddWithValue("id",Guid.CreateVersion7());command.Parameters.AddWithValue("name",name);command.Parameters.AddWithValue("now",Now);
        if(allowed)Assert.Equal(1,await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        else Assert.Equal(PostgresErrorCodes.CheckViolation,(await Assert.ThrowsAsync<PostgresException>(()=>command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken))).SqlState);
    }
    [Fact]
    public async Task InvalidLease_PreventsMigrationsAndResetEffects()
    {
        await Prepare(); Assert.Equal(1, await database.SetSentinelAndCountAsync());
        await database.SetLeaseForSafetyScenarioAsync(false);
        try
        {
            await using var connection = new NpgsqlConnection(database.AdminConnectionString); await connection.OpenAsync(TestContext.Current.CancellationToken);
            Assert.Equal("TestSafety.LeaseMismatch", (await Assert.ThrowsAsync<InvalidOperationException>(() => database.MigrateIdentityAsync(connection))).Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.ResetIdentityAsync());
        }
        finally { await database.SetLeaseForSafetyScenarioAsync(true); }
        Assert.Equal(1, await database.CountSentinelAsync());
    }
    [Fact]
    public async Task NonOwnedTarget_PreventsMigrationEffects()
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(database.AdminConnectionString) { Username = new NpgsqlConnectionStringBuilder(database.RuntimeConnectionString).Username, Password = new NpgsqlConnectionStringBuilder(database.RuntimeConnectionString).Password }.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal("TestSafety.TargetNotOwned", (await Assert.ThrowsAsync<InvalidOperationException>(() => database.MigrateIdentityAsync(connection))).Message);

    }
    [Fact]
    public async Task RuntimePrincipal_CanWriteOwnedTablesButCannotDdlOrWriteForeignSchema()
    {
        await Prepare(); await using var context = Runtime();
        AddAccount(context, Guid.CreateVersion7(), "+201012345678"); await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Single(await context.Accounts.ToListAsync(TestContext.Current.CancellationToken));
        await using var connection = new NpgsqlConnection(database.ModuleConnectionString); await connection.OpenAsync(TestContext.Current.CancellationToken);
        foreach (var sql in new[] { "CREATE TABLE identity_access.forbidden(value int)", "CREATE SCHEMA forbidden", "INSERT INTO test_support.sentinel VALUES (2)", "DELETE FROM identity_access.__ef_migrations_history" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        }
    }
    [Theory]
    [InlineData("phone")] [InlineData("email")] [InlineData("person")]
    public async Task UniqueContactAndPersonConstraints_RejectDuplicates(string scenario)
    {
        await Prepare(); var person = Guid.CreateVersion7();
        await using (var context = Runtime())
        { AddAccount(context, person, "+201012345678", "unique@example.com"); await context.SaveChangesAsync(TestContext.Current.CancellationToken); }
        await using var other = Runtime();
        AddAccount(other, scenario == "person" ? person : Guid.CreateVersion7(), scenario == "phone" ? "+201012345678" : "+201112345678", scenario == "email" ? "unique@example.com" : null, scenario != "person");
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }
    [Fact]
    public async Task MissingPersonForeignKey_IsRejected()
    {
        await Prepare(); await using var context = Runtime();
        AddAccount(context, Guid.CreateVersion7(), "+201012345678", newPerson: false);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }
    [Fact]
    public async Task OneActiveChallengeConstraint_AllowsReplacementAfterInvalidation()
    {
        await Prepare(); await using var context = Runtime();
        var old = new OtpChallenge(Guid.CreateVersion7(), "+201012345678", new byte[32], "v1", Now, 300);
        context.Challenges.Add(old); await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using (var other = Runtime())
        {
            other.Challenges.Add(new OtpChallenge(Guid.CreateVersion7(), "+201012345678", new byte[32], "v1", Now, 300));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        }
        old.Invalidate(); await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.Challenges.Add(new OtpChallenge(Guid.CreateVersion7(), "+201012345678", new byte[32], "v1", Now, 300));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken); Assert.Equal(2, await context.Challenges.CountAsync(TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task PersistentTargetBudget_RoundTripsUtcHistory()
    {
        await Prepare(); var digest = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        await using (var context = Runtime())
        {
            var target = new VerificationTarget(digest); target.ReserveIssue(Now, 900, 3, 60);
            target.ReserveVerification(Now, 900, 10); context.Targets.Add(target); await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using var reload = Runtime(); var saved = await reload.Targets.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Now, Assert.Single(saved.IssuesUtc)); Assert.Equal(Now, Assert.Single(saved.VerificationsUtc));
        Assert.False(saved.ReserveIssue(Now, 900, 3, 60).IsSuccess);
    }
    [Fact]
    public async Task DatabaseChecks_RejectInvalidSecurityState()
    {
        await Prepare(); await using var context = Runtime(); AddAccount(context, Guid.CreateVersion7(), "+201012345678"); await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var connection = new NpgsqlConnection(database.ModuleConnectionString); await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("UPDATE identity_access.user_accounts SET security_version=0", connection);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken))).SqlState);
    }
}
