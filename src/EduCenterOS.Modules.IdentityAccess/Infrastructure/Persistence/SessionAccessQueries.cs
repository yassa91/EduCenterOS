using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;

internal static class SessionAccessQueries
{
    internal static IQueryable<SessionAccessState> ForSession(IdentityAccessDbContext context, Guid sessionId) =>
        context.Sessions.AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Join(context.Accounts.AsNoTracking(), session => session.UserAccountId, account => account.Id,
                (session, account) => new SessionAccessState
                {
                    AccountId = account.Id,
                    Status = account.Status,
                    AccountCreatedAtUtc = account.CreatedAtUtc,
                    PhoneVerifiedAtUtc = account.PhoneVerifiedAtUtc,
                    AccountSecurityVersion = account.SecurityVersion,
                    SessionId = session.Id,
                    SessionAccountId = session.UserAccountId,
                    SessionSecurityVersion = session.SecurityVersionAtAuthentication,
                    Lifetime = new SessionLifetime(
                        session.CreatedAtUtc,
                        session.IdleExpiresAtUtc,
                        session.AbsoluteExpiresAtUtc,
                        session.RevokedAtUtc
                    )
                });
}
