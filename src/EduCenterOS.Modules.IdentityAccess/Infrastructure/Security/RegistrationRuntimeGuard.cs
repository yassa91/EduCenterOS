using System.Security.Cryptography;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;

internal sealed class RegistrationRuntimeGuard(IdentityAccessRuntimeSettings settings, IDbContextFactory<IdentityAccessDbContext> factory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(724136800)", cancellationToken);
            var fingerprint = SHA256.HashData(settings.PartitionKey);
            var saved = await context.Set<RateKeyBinding>().SingleOrDefaultAsync(cancellationToken);

            if (saved is null)
            {
                context.Add(new RateKeyBinding(fingerprint));
                await context.SaveChangesAsync(cancellationToken);
            }
            else if (!CryptographicOperations.FixedTimeEquals(fingerprint, saved.Fingerprint)) throw new InvalidOperationException();

            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Configuration.IdentityAccessRuntimeUnavailable: migrations and stable security-key binding are required.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
