using System.Security.Cryptography;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using System.Text.Json;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using Xunit;

namespace EduCenterOS.IntegrationTests.Infrastructure;

public sealed class OwnedPostgresFixture : IAsyncLifetime
{
    private readonly Guid lease = Guid.NewGuid();
    internal byte[] OtpKey { get; } = RandomNumberGenerator.GetBytes(32);
    internal byte[] PartitionKey { get; } = RandomNumberGenerator.GetBytes(32);
    private readonly string ownerConnectionString;
    private readonly string probeConnectionString;
    private readonly string moduleConnectionString;
    private NpgsqlConnection? leaseConnection;
    private int leaseBackendPid;
    private const long LeaseLock = 68202910413001;
    internal SupabaseDatabaseTarget Target { get; }
    public string DatabaseName => "postgres";

    public OwnedPostgresFixture()
    {
        try
        {
            var raw = Environment.GetEnvironmentVariable("EDUCENTEROS_TEST_DATABASE_SNAPSHOT");

            if (raw is null || raw.Length > 16384) throw new InvalidOperationException();

            using var json = JsonDocument.Parse(raw);
            var root = json.RootElement;
            var names = root.EnumerateObject().Select(p => p.Name).ToArray();

            if (
                names.Length != 4 ||
                names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4 ||
                names.Except(new[] { "databaseTarget", "owner", "probe", "runtime" }).Any()
            )
                throw new InvalidOperationException();

            var targetJson = root.GetProperty("databaseTarget");
            var targetNames = targetJson.EnumerateObject().Select(p => p.Name).ToArray();

            if (
                targetNames.Length != 4 ||
                targetNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4 ||
                targetNames.Except(new[] { "projectReference", "host", "environment", "serverMajor" }).Any()
            )
                throw new InvalidOperationException();

            Target = JsonSerializer.Deserialize<SupabaseDatabaseTarget>(targetJson.GetRawText(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? throw new InvalidOperationException();
            ownerConnectionString = Target.Validate(root.GetProperty("owner").GetString()!, "Testing", "owner").ConnectionString;
            probeConnectionString = Target.Validate(root.GetProperty("probe").GetString()!, "Testing", "probe").ConnectionString;
            moduleConnectionString = Target.Validate(root.GetProperty("runtime").GetString()!, "Testing", "runtime").ConnectionString;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("TestSafety.TrustedCloudSnapshotRequired");
        }
    }

    public string AdminConnectionString => ownerConnectionString;
    public string RuntimeConnectionString => probeConnectionString;
    internal string ModuleConnectionString => moduleConnectionString;

    internal async Task MigrateIdentityAsync(NpgsqlConnection connection, string? initialMigration = null)
    {
        await EnsureOwnedAsync(connection);
        await using var context = new IdentityAccessDbContext(IdentityAccessDbContext.Options(AdminConnectionString));

        if (initialMigration is not null)
        {
            if (initialMigration != "20261001150837_InitialRegistration") throw new InvalidOperationException("TestSafety.UnreviewedMigrationTarget");

            await context.GetService<IMigrator>().MigrateAsync(initialMigration);

            return;
        }

        await context.Database.MigrateAsync();
        var runtime = Target.Role("runtime");
        await using var grant = new NpgsqlCommand(
            $"""
            REVOKE ALL ON SCHEMA identity_access FROM PUBLIC, anon, authenticated, service_role;
            REVOKE ALL ON ALL TABLES IN SCHEMA identity_access FROM PUBLIC, anon, authenticated, service_role;
            GRANT USAGE ON SCHEMA identity_access TO {runtime};
            GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.person_identities, identity_access.user_accounts,
                identity_access.otp_challenges, identity_access.verification_targets, identity_access.rate_key_binding TO {runtime};
            GRANT SELECT ON identity_access.__ef_migrations_history TO {runtime};
            """,
            connection
        );
        await grant.ExecuteNonQueryAsync();
    }

    internal async Task ResetIdentityAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await EnsureOwnedAsync(connection);
        await using var command = new NpgsqlCommand(
            "TRUNCATE identity_access.user_accounts, identity_access.person_identities, identity_access.otp_challenges, identity_access.verification_targets",
            connection
        );
        await command.ExecuteNonQueryAsync();
    }

    internal async Task PrepareIdentityAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await MigrateIdentityAsync(connection);
    }

    public async ValueTask InitializeAsync()
    {
        try
        {
            leaseConnection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(AdminConnectionString) { Pooling = false }.ConnectionString);
            await leaseConnection.OpenAsync();
            await VerifyIdentityAsync(leaseConnection);

            await using (var claim = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key), pg_backend_pid()", leaseConnection))
            {
                claim.Parameters.AddWithValue("key", LeaseLock);
                await using var reader = await claim.ExecuteReaderAsync();

                if (!await reader.ReadAsync() || !reader.GetBoolean(0)) throw new InvalidOperationException("TestSafety.CloudProjectAlreadyInUse");

                leaseBackendPid = reader.GetInt32(1);
            }

            await VerifyLeaseSessionAsync(leaseConnection);
            await using var command = new NpgsqlCommand(
                """
                DROP SCHEMA IF EXISTS identity_access CASCADE;
                DROP SCHEMA IF EXISTS test_support CASCADE;
                CREATE SCHEMA test_support;
                REVOKE ALL ON SCHEMA test_support FROM PUBLIC, anon, authenticated, service_role;
                CREATE TABLE test_support.run_ownership (lease uuid NOT NULL);
                CREATE TABLE test_support.sentinel (value integer NOT NULL);
                INSERT INTO test_support.run_ownership VALUES (@lease);
                """,
                leaseConnection
            );
            command.Parameters.AddWithValue("lease", lease);
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            await ReleaseLeaseAsync();

            throw;
        }
    }

    private async Task VerifyIdentityAsync(NpgsqlConnection connection)
    {
        var target = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        var expected = new NpgsqlConnectionStringBuilder(AdminConnectionString);

        if (
            target.Host != expected.Host ||
            target.Port != expected.Port ||
            target.Database != DatabaseName ||
            target.Username != expected.Username ||
            target.SslMode != SslMode.VerifyFull
        )
            throw new InvalidOperationException("TestSafety.TargetNotOwned");

        await using var command = new NpgsqlCommand(
            """
            SELECT current_database(), current_user, current_setting('server_version_num')::integer,
                (SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolbypassrls FROM pg_roles WHERE rolname=current_user),
                (SELECT count(*) FROM educenteros_test_control.target WHERE project_reference=@project AND environment='Testing'),
                (SELECT count(*) FROM educenteros_test_control.target)
            """,
            connection
        );
        command.Parameters.AddWithValue("project", Target.ProjectReference);
        await using var reader = await command.ExecuteReaderAsync();

        if (
            !await reader.ReadAsync() ||
            reader.GetString(0) != DatabaseName ||
            reader.GetString(1) != Target.Role("owner") ||
            reader.GetInt32(2) / 10000 != Target.ServerMajor ||
            reader.GetBoolean(3) ||
            reader.GetInt64(4) != 1 ||
            reader.GetInt64(5) != 1
        )
            throw new InvalidOperationException("TestSafety.ActualIdentityMismatch");
    }

    private async Task VerifyLeaseSessionAsync(NpgsqlConnection connection)
    {
        if (leaseConnection?.State != System.Data.ConnectionState.Open || leaseBackendPid == 0)
            throw new InvalidOperationException("TestSafety.LeaseSessionLost");

        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS(SELECT FROM pg_locks WHERE locktype='advisory' AND pid=@pid AND granted
                AND classid=(@key >> 32)::oid AND objid=(@key & 4294967295)::oid AND objsubid=1)
            """,
            connection
        );
        command.Parameters.AddWithValue("pid", leaseBackendPid);
        command.Parameters.AddWithValue("key", LeaseLock);

        if (await command.ExecuteScalarAsync() is not true) throw new InvalidOperationException("TestSafety.LeaseSessionLost");
    }

    public async Task EnsureOwnedAsync(NpgsqlConnection connection)
    {
        await VerifyIdentityAsync(connection);
        await VerifyLeaseSessionAsync(connection);
        await using var command = new NpgsqlCommand("SELECT lease FROM test_support.run_ownership", connection);

        if (await command.ExecuteScalarAsync() is not Guid marker || marker != lease)
            throw new InvalidOperationException("TestSafety.LeaseMismatch");
    }

    internal async Task ResetMigrationSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await EnsureOwnedAsync(connection);
        await using var command = new NpgsqlCommand("DROP SCHEMA IF EXISTS identity_access CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task ResetSentinelAsync(NpgsqlConnection connection)
    {
        await EnsureOwnedAsync(connection);
        await using var command = new NpgsqlCommand("TRUNCATE TABLE test_support.sentinel", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> SetSentinelAndCountAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await ResetSentinelAsync(connection);
        await using var command = new NpgsqlCommand("INSERT INTO test_support.sentinel VALUES (1); SELECT count(*)::integer FROM test_support.sentinel", connection);

        return (int)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("TestSafety.CountMissing"));
    }

    public async Task<int> CountSentinelAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await EnsureOwnedAsync(connection);
        await using var command = new NpgsqlCommand("SELECT count(*)::integer FROM test_support.sentinel", connection);

        return (int)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("TestSafety.CountMissing"));
    }

    public async Task SetLeaseForSafetyScenarioAsync(bool valid)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        // Provisioning identity is checked independently before the controlled lease fault/repair.
        await VerifyIdentityAsync(connection);
        await using var command = new NpgsqlCommand("UPDATE test_support.run_ownership SET lease = @lease", connection);
        command.Parameters.AddWithValue("lease", valid ? lease : Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (leaseConnection is null) return;

        try
        {
            await EnsureOwnedAsync(leaseConnection);
            await using var command = new NpgsqlCommand("DROP SCHEMA IF EXISTS identity_access CASCADE; DROP SCHEMA test_support CASCADE", leaseConnection);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await ReleaseLeaseAsync();
        }
    }

    private async Task ReleaseLeaseAsync()
    {
        if (leaseConnection is null) return;

        try
        {
            // Explicit release is needed even when a session pooler keeps its backend connection.
            if (leaseBackendPid != 0 && leaseConnection.State == System.Data.ConnectionState.Open)
            {
                await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", leaseConnection);
                release.Parameters.AddWithValue("key", LeaseLock);
                await release.ExecuteScalarAsync();
            }
        }
        finally
        {
            await leaseConnection.DisposeAsync();
            leaseConnection = null;
            leaseBackendPid = 0;
        }
    }

    internal static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "EduCenterOS.sln")))
                return directory.FullName;

        throw new InvalidOperationException("TestInfrastructure.ProjectRootMissing");
    }
}
