using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EduCenterOS.Api.Infrastructure;

internal sealed class DatabaseProbeOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 3;
}

internal sealed class DatabaseHealthCheck(IOptions<DatabaseProbeOptions> options, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            await using var connection = new NpgsqlConnection(options.Value.ConnectionString);
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
