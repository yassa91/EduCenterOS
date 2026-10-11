using EduCenterOS.BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;

internal static class TransactionFailures
{
    internal static async Task RollbackAsync(IDbContextTransaction transaction)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        try
        {
            await transaction.RollbackAsync(timeout.Token);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Infrastructure.TransactionOutcomeUnknown");
        }
    }

    // Call only after confirmed rollback. Commit-started failures must propagate.
    internal static Error? Classify(Exception exception)
    {
        var postgres = exception as PostgresException ?? exception.InnerException as PostgresException;

        if (postgres?.SqlState is "55P03" or "40P01" or "40001")
            return new Error(
                "Infrastructure.Busy",
                ErrorCategory.ServiceUnavailable,
                "The service is temporarily busy.",
                1
            );

        if (
            postgres?.SqlState is "57014" or "25P04" ||
            exception is TimeoutException ||
            exception.InnerException is TimeoutException
        )
            return new Error(
                "Infrastructure.Timeout",
                ErrorCategory.ServiceUnavailable,
                "The service is temporarily unavailable.",
                1
            );

        return null;
    }
}
