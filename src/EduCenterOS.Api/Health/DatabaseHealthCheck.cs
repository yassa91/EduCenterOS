using EduCenterOS.Api.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EduCenterOS.Api.Health;

internal sealed class DatabaseHealthCheck(IOptions<DatabaseProbeOptions> options, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            // Bound driver-level waits as well as cancellation; an unresponsive server can
            // otherwise retain the connection-string's longer default connect timeout.
            var connectionSettings = new NpgsqlConnectionStringBuilder(options.Value.ConnectionString)
            {
                Timeout = options.Value.TimeoutSeconds,
                CommandTimeout = options.Value.TimeoutSeconds
            };
            await using var connection = new NpgsqlConnection(connectionSettings.ConnectionString);
            await connection.OpenAsync(timeout.Token);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            logger.LogWarning(new EventId(1002, "DatabaseProbeUnavailable"),
                "Required database probe failed ({FailureKind}).", exception.GetType().Name);
            return HealthCheckResult.Unhealthy();
        }
    }
}
