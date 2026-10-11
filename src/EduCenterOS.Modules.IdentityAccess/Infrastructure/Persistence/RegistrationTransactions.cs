using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;

internal enum RegistrationOperation
{
    Issue,
    Verify,
    Register
}

// Shared only by this owning security flow, not a generic repository/UnitOfWork framework.
internal sealed class RegistrationTransactions(IDbContextFactory<IdentityAccessDbContext> factory, IClock clock, RegistrationCryptography crypto)
{
    internal async Task<Result<T>> RunAsync<T>(
        RegistrationOperation operation,
        string phone,
        Func<IdentityAccessDbContext, DateTimeOffset, CancellationToken, Task<Result<T>>> action,
        CancellationToken cancellationToken
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        IDbContextTransaction? transaction = null;
        var commitStarted = false;

        try
        {
            transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='2s'; SET LOCAL statement_timeout='5s'", cancellationToken);
            var key = crypto.TargetLock(phone);
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
            var now = clock.UtcNow;
            var result = await action(context, now, cancellationToken);
            // Expected verify failures must commit attempts/quotas, while registration failure writes nothing.
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

            var postgres = exception as PostgresException ?? exception.InnerException as PostgresException;

            if (
                operation == RegistrationOperation.Register &&
                postgres?.SqlState == PostgresErrorCodes.UniqueViolation &&
                postgres.ConstraintName is "IX_user_accounts_normalized_phone_number" or "IX_user_accounts_normalized_email_address" or "IX_user_accounts_person_identity_id"
            )
                return Result<T>.Failure(RegistrationErrors.RegistrationRejected);

            var failure = TransactionFailures.Classify(exception);

            if (failure is not null) return Result<T>.Failure(failure);

            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    internal static async Task<VerificationTarget> TargetAsync(IdentityAccessDbContext context, byte[] digest, CancellationToken cancellationToken)
    {
        var target = await context.Targets.SingleOrDefaultAsync(value => value.Digest == digest, cancellationToken);

        if (target is not null) return target;

        target = new VerificationTarget(digest);
        context.Targets.Add(target);

        return target;
    }
}
