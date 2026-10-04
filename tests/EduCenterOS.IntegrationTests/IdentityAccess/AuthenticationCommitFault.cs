using System.Data.Common;
using EduCenterOS.Modules.IdentityAccess.Domain;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

internal sealed class AuthenticationCommitFault(bool afterCommit) : DbTransactionInterceptor
{
    internal bool Enabled { get; set; }
    internal int CommitCalls { get; private set; }

    private bool Applies(DbContextEventData eventData) => Enabled && eventData.Context!.ChangeTracker.Entries<UserSession>().Any();

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (Applies(eventData))
        {
            CommitCalls++;

            if (!afterCommit) throw new IOException("synthetic-auth-commit-fault");
        }

        return ValueTask.FromResult(result);
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (Applies(eventData) && afterCommit) throw new IOException("synthetic-auth-commit-fault");

        return Task.CompletedTask;
    }
}
