using System.Security.Cryptography;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EduCenterOS.IntegrationTests.Infrastructure;

public sealed class OwnedPostgresFixture : IAsyncLifetime
{
    private readonly Guid lease = Guid.NewGuid();
    private readonly string runtimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly PostgreSqlContainer container;
    private string? ownedContainerId;
    public string DatabaseName { get; } = $"educenteros_{Guid.NewGuid():N}_tests";

    public OwnedPostgresFixture()
    {
        var root = FindRoot();
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "infra/runtime.json")));
        var image = manifest.RootElement.GetProperty("postgresImage").GetString()
            ?? throw new InvalidOperationException("TestInfrastructure.ImageMissing");
        container = new PostgreSqlBuilder(image)
            .WithDatabase(DatabaseName)
            .WithUsername("educenteros_tests_admin")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
            .WithLabel("educenteros-test-lease", lease.ToString("N"))
            .WithCreateParameterModifier(parameters =>
            {
                var bindings = parameters.HostConfig?.PortBindings
                    ?? throw new InvalidOperationException("TestInfrastructure.PortBindingsMissing");
                foreach (var binding in bindings["5432/tcp"])
                    binding.HostIP = "127.0.0.1";
            })
            .Build();
    }

    public string AdminConnectionString => new NpgsqlConnectionStringBuilder(container.GetConnectionString())
    {
        Host = "127.0.0.1",
        IncludeErrorDetail = false
    }.ConnectionString;

    public string RuntimeConnectionString => new NpgsqlConnectionStringBuilder(AdminConnectionString)
    {
        Username = "educenteros_runtime_probe",
        Password = runtimePassword
    }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        try
        {
            await container.StartAsync();
            ownedContainerId = container.Id;
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync();
            await VerifyIdentityAsync(connection);
            await using var command = new NpgsqlCommand("""
                CREATE SCHEMA test_support;
                CREATE TABLE test_support.run_ownership (lease uuid NOT NULL);
                CREATE TABLE test_support.sentinel (value integer NOT NULL);
                INSERT INTO test_support.run_ownership VALUES (@lease);
                """, connection);
            command.Parameters.AddWithValue("lease", lease);
            await command.ExecuteNonQueryAsync();
            await using var role = new NpgsqlCommand(
                $"CREATE ROLE educenteros_runtime_probe LOGIN PASSWORD '{runtimePassword}'; GRANT CONNECT ON DATABASE \"{DatabaseName}\" TO educenteros_runtime_probe;", connection);
            await role.ExecuteNonQueryAsync();
        }
        catch (Exception originalFailure)
        {
            try
            {
                await container.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("TestInfrastructure.ProvisioningAndCleanupFailed", originalFailure, cleanupFailure);
            }
            throw;
        }
    }

    private async Task VerifyIdentityAsync(NpgsqlConnection connection)
    {
        var target = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        var expected = new NpgsqlConnectionStringBuilder(AdminConnectionString);
        if (ownedContainerId is null || container.Id != ownedContainerId || target.Host != expected.Host
            || target.Port != expected.Port || target.Database != DatabaseName || target.Username != expected.Username
            || !DatabaseName.EndsWith("_tests", StringComparison.Ordinal))
            throw new InvalidOperationException("TestSafety.TargetNotOwned");

        await using var command = new NpgsqlCommand("SELECT current_database(), current_user, current_setting('server_version_num')::integer", connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetString(0) != DatabaseName || reader.GetString(1) != expected.Username || reader.GetInt32(2) / 10000 != 18)
            throw new InvalidOperationException("TestSafety.ActualIdentityMismatch");
    }

    public async Task EnsureOwnedAsync(NpgsqlConnection connection)
    {
        await VerifyIdentityAsync(connection);
        await using var command = new NpgsqlCommand("SELECT lease FROM test_support.run_ownership", connection);
        var actual = await command.ExecuteScalarAsync();
        if (actual is not Guid marker || marker != lease)
            throw new InvalidOperationException("TestSafety.LeaseMismatch");
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

    public Task PauseAsync() => container.PauseAsync();
    public Task UnpauseAsync() => container.UnpauseAsync();

    public async ValueTask DisposeAsync()
    {
        if (ownedContainerId is not null && container.Id != ownedContainerId)
            throw new InvalidOperationException("TestSafety.ContainerIdentityMismatch");
        // Docker disposal targets this fixture-owned ID; no shared-server SQL cleanup.
        await container.DisposeAsync();
    }

    internal static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "EduCenterOS.sln")))
                return directory.FullName;
        throw new InvalidOperationException("TestInfrastructure.ProjectRootMissing");
    }
}
