using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Features.LogoutAll;

internal sealed class LogoutAllHandler(ICurrentAccountActor current, AuthenticationTransactions transactions, IClock clock)
{
    internal async Task<Result<bool>> HandleAsync(CancellationToken cancellationToken)
    {
        var actor = current.Require();

        return await transactions.RunAsync(async (context, token) =>
        {
            var account = await AuthenticationTransactions.LockAccountAsync(context, actor.AccountId, token);

            if (account is null) return Result<bool>.Failure(AuthenticationErrors.Rejected);

            var sessions = await context.Sessions.FromSqlInterpolated(
                $"SELECT * FROM identity_access.user_sessions WHERE user_account_id={actor.AccountId} ORDER BY id FOR UPDATE"
            ).ToListAsync(token);
            var now = clock.UtcNow;
            var own = sessions.SingleOrDefault(session => session.Id == actor.SessionId);

            if (own is null || !SessionAccessState.From(account, own).AcceptsActor(actor, now))
                return Result<bool>.Failure(AuthenticationErrors.Rejected);

            foreach (var session in sessions)
            {
                session.Revoke(now, "LogoutAll");
            }

            return Result<bool>.Success(true);
        }, cancellationToken);
    }
}
