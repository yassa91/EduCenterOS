using System.Collections.Concurrent;
using System.Data.Common;
using EduCenterOS.Modules.IdentityAccess.Domain;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

internal sealed class AuthenticationAccountCommitGate : DbTransactionInterceptor
{
    private int arrivals;
    internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<UserAccount>().Any() && Interlocked.Increment(ref arrivals) == 1)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }

        return result;
    }
}

internal sealed class AuthenticationAccountLockProbe : DbCommandInterceptor
{
    internal bool Enabled { get; set; } = true;
    private int arrivals;
    internal TaskCompletionSource<int> FirstBackendPid { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<int> SecondBackendPid { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal ConcurrentBag<Guid> Contexts { get; } = new();

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (
            Enabled &&
            command.CommandText.Contains("identity_access.user_accounts", StringComparison.Ordinal) &&
            command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
        )
        {
            Contexts.Add(eventData.Context!.ContextId.InstanceId);
            await using var pid = new NpgsqlCommand("SELECT pg_backend_pid()", (NpgsqlConnection)command.Connection!, (NpgsqlTransaction?)command.Transaction);
            var backend = (int)(await pid.ExecuteScalarAsync(cancellationToken))!;

            var arrival = Interlocked.Increment(ref arrivals);

            if (arrival == 1) FirstBackendPid.TrySetResult(backend);
            if (arrival == 2) SecondBackendPid.TrySetResult(backend);
        }

        return result;
    }
}
