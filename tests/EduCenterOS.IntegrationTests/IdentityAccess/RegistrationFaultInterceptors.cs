using System.Collections.Concurrent;
using System.Data.Common;
using EduCenterOS.Modules.IdentityAccess.Domain;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

internal sealed class AccountSaveOverlapInterceptor : SaveChangesInterceptor
{
    private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;
    internal ConcurrentBag<Guid> Contexts { get; } = new();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<UserAccount>().Any())
        {
            Contexts.Add(eventData.Context.ContextId.InstanceId);

            if (Interlocked.Increment(ref arrivals) == 2) both.TrySetResult();

            await both.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        return result;
    }
}

internal sealed class RegistrationCommitFaultInterceptor(bool afterCommit) : DbTransactionInterceptor
{
    internal bool Enabled { get; set; }
    internal int CommitCalls { get; private set; }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        if (Enabled)
        {
            CommitCalls++;

            if (!afterCommit) throw new IOException("synthetic-secret-commit-fault");
        }

        return ValueTask.FromResult(result);
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (Enabled && afterCommit) throw new IOException("synthetic-secret-commit-fault");

        return Task.CompletedTask;
    }
}
