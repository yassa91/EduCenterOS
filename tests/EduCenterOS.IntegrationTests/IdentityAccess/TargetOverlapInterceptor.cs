using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

internal sealed class TargetOverlapInterceptor : DbCommandInterceptor
{
    private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;
    internal ConcurrentBag<Guid> Contexts { get; } = new();
    internal bool Enabled { get; set; }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (Enabled && command.CommandText.Contains("pg_advisory_xact_lock(@", StringComparison.Ordinal))
        {
            Contexts.Add(eventData.Context!.ContextId.InstanceId);

            if (Interlocked.Increment(ref arrivals) == 2) both.TrySetResult();

            await both.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        return result;
    }
}
