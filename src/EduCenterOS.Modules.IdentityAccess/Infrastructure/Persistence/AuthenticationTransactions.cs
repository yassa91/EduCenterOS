using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;

internal sealed class AuthenticationTransactions(IDbContextFactory<IdentityAccessDbContext> factory)
{
    internal async Task<Result<T>> RunAsync<T>(Func<IdentityAccessDbContext, CancellationToken, Task<Result<T>>> action, CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        IDbContextTransaction? transaction = null;
        var commitStarted = false;

        try
        {
            transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='2s'; SET LOCAL statement_timeout='5s'", cancellationToken);
            var result = await action(context, cancellationToken);
            // Expected failures can own security writes (attempts, quotas, replay revocation).
            await context.SaveChangesAsync(cancellationToken);
            commitStarted = true;
            await transaction.CommitAsync(cancellationToken);

            return result;
        }
        catch (Exception exception)
        {
            if (transaction is null || commitStarted) throw;

            await TransactionFailures.RollbackAsync(transaction);

            if (cancellationToken.IsCancellationRequested) throw;

            var failure = TransactionFailures.Classify(exception);

            if (failure is not null) return Result<T>.Failure(failure);

            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    internal static Task<UserAccount?> LockAccountAsync(IdentityAccessDbContext context, Guid id, CancellationToken cancellationToken) =>
        context.Accounts.FromSqlInterpolated($"SELECT * FROM identity_access.user_accounts WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);

    internal static Task<UserSession?> LockSessionAsync(IdentityAccessDbContext context, Guid id, CancellationToken cancellationToken) =>
        context.Sessions.FromSqlInterpolated($"SELECT * FROM identity_access.user_sessions WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);

    internal static Task<RefreshTokenRecord?> LockRefreshAsync(IdentityAccessDbContext context, Guid id, CancellationToken cancellationToken) =>
        context.RefreshTokens.FromSqlInterpolated($"SELECT * FROM identity_access.refresh_token_records WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
}
