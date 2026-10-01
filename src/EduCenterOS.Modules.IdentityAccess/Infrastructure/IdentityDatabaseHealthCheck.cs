using EduCenterOS.Modules.IdentityAccess.Contracts;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
namespace EduCenterOS.Modules.IdentityAccess.Infrastructure;

internal sealed class IdentityDatabaseHealthCheck(IdentityAccessRuntimeSettings settings) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var options = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { Timeout = 3, CommandTimeout = 3 };
            await using var connection = new NpgsqlConnection(options.ConnectionString); await connection.OpenAsync(timeout.Token);
            await using var command = new NpgsqlCommand("SELECT 1 FROM identity_access.rate_key_binding WHERE id=1", connection);
            return await command.ExecuteScalarAsync(timeout.Token) is null ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is NpgsqlException or OperationCanceledException or TimeoutException) { return HealthCheckResult.Unhealthy(); }
    }
}
