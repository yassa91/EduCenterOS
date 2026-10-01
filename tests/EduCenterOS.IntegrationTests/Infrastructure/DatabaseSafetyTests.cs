using Npgsql;
using Xunit;

namespace EduCenterOS.IntegrationTests.Infrastructure;

public sealed class DatabaseSafetyTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    [Fact]
    public async Task RuntimeRole_ConnectsWithoutSuperuserPrivileges()
    {
        await using var connection = new NpgsqlConnection(database.RuntimeConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT current_database(), rolsuper FROM pg_roles WHERE rolname = current_user", connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(database.DatabaseName, reader.GetString(0));
        Assert.False(reader.GetBoolean(1));
    }

    [Fact]
    public async Task Reset_NonOwnedDatabaseIsRejectedBeforeChangingOwnedRows()
    {
        Assert.Equal(1, await database.SetSentinelAndCountAsync());
        var target = new NpgsqlConnectionStringBuilder(database.AdminConnectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => database.ResetSentinelAsync(connection));
        Assert.Equal("TestSafety.TargetNotOwned", exception.Message);
        Assert.Equal(1, await database.CountSentinelAsync());
    }

    [Fact]
    public async Task Reset_InvalidLeaseIsRejectedBeforeChangingRows()
    {
        Assert.Equal(1, await database.SetSentinelAndCountAsync());
        await database.SetLeaseForSafetyScenarioAsync(valid: false);
        try
        {
            await using var connection = new NpgsqlConnection(database.AdminConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => database.ResetSentinelAsync(connection));
            Assert.Equal("TestSafety.LeaseMismatch", exception.Message);
        }
        finally
        {
            await database.SetLeaseForSafetyScenarioAsync(valid: true);
        }
        Assert.Equal(1, await database.CountSentinelAsync());
    }

    [Fact]
    public async Task Reset_OwnedIdentityAndLeasePermitOnlyManifestTableCleanup()
    {
        Assert.Equal(1, await database.SetSentinelAndCountAsync());
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await database.ResetSentinelAsync(connection);
        Assert.Equal(0, await database.CountSentinelAsync());
        await database.EnsureOwnedAsync(connection);
    }
}
