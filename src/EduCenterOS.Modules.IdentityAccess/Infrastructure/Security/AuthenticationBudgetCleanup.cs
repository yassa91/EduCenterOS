using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;

internal sealed class AuthenticationBudgetCleanup(IDbContextFactory<IdentityAccessDbContext> factory, IClock clock, ILogger<AuthenticationBudgetCleanup> logger) : BackgroundService
{
    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        // Maximum configured login window is one hour. Only inactive budgets older than a day are eligible.
        var cutoff = clock.UtcNow.AddDays(-1);
        await context.Database.ExecuteSqlInterpolatedAsync($"WITH expired AS (SELECT digest FROM identity_access.login_targets WHERE last_attempt_at_utc < {cutoff} ORDER BY last_attempt_at_utc,digest LIMIT 100 FOR UPDATE SKIP LOCKED) DELETE FROM identity_access.login_targets AS target USING expired WHERE target.digest=expired.digest AND target.last_attempt_at_utc < {cutoff}", cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                logger.LogWarning(new EventId(2110, "AuthenticationBudgetCleanupUnavailable"), "Authentication budget cleanup is temporarily unavailable.");
            }
        }
    }
}
