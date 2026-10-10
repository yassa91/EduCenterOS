using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Features.RevokeSession;

internal sealed class RevokeSessionHandler(ICurrentAccountActor current, AuthenticationTransactions transactions, IClock clock)
{
    internal async Task<Result<bool>> HandleAsync(Guid target, string? presentedRefresh, CancellationToken cancellationToken)
    {
        var actor = current.Require();

        return await transactions.RunAsync(async (context, token) =>
        {
            var account = await AuthenticationTransactions.LockAccountAsync(context, actor.AccountId, token);

            if (account is null) return Result<bool>.Failure(AuthenticationErrors.Rejected);

            var ids = new[] { actor.SessionId, target }.Distinct().ToArray();
            var sessions = await context.Sessions.FromSqlInterpolated(
                $"SELECT * FROM identity_access.user_sessions WHERE user_account_id={actor.AccountId} AND id=ANY({ids}) ORDER BY id FOR UPDATE"
            ).ToListAsync(token);
            var now = clock.UtcNow;
            var own = sessions.SingleOrDefault(session => session.Id == actor.SessionId);

            if (own is null || !SessionAccessState.From(account, own).AcceptsActor(actor, now))
                return Result<bool>.Failure(AuthenticationErrors.Rejected);

            var selected = sessions.SingleOrDefault(session => session.Id == target);

            if (selected is null) return Result<bool>.Failure(AuthenticationErrors.SessionNotFound);

            selected.Revoke(now, "UserRequest");
            var presentedBelongsToTarget = false;

            if (RegistrationCryptography.IsProof(presentedRefresh))
            {
                var hash = AuthenticationTokens.HashRefresh(presentedRefresh!);
                presentedBelongsToTarget = await context.RefreshTokens.AsNoTracking().AnyAsync(
                    credential => credential.UserSessionId == selected.Id && credential.TokenHash == hash,
                    token
                );
            }

            return Result<bool>.Success(presentedBelongsToTarget);
        }, cancellationToken);
    }
}
