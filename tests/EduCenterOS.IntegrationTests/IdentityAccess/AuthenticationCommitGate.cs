using System.Data.Common;
using EduCenterOS.Modules.IdentityAccess.Domain;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

internal sealed class AuthenticationCommitGate : DbTransactionInterceptor
{
    internal bool Enabled { get; set; }
    internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (Enabled && eventData.Context!.ChangeTracker.Entries<UserSession>().Any())
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }

        return result;
    }
}
