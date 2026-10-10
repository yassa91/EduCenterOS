using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Features.ListSessions;

internal static class SessionHistoryQueries
{
    internal static IQueryable<SessionItem> PageFor(IdentityAccessDbContext context, AuthenticatedActor actor, SessionPage page)
    {
        // EF Skip takes int; the HTTP contract permits a larger 64-bit offset.
        var offset = (long)(page.Page - 1) * page.PageSize;
        var sessions = context.Sessions.FromSqlInterpolated(
            $"SELECT * FROM identity_access.user_sessions WHERE user_account_id={actor.AccountId} ORDER BY created_at_utc DESC,id DESC LIMIT {page.PageSize} OFFSET {offset}"
        );

        return sessions.AsNoTracking()
            .OrderByDescending(session => session.CreatedAtUtc)
            .ThenByDescending(session => session.Id)
            .Select(session => new SessionItem(
                session.Id, session.CreatedAtUtc, session.AuthenticatedAtUtc, session.LastSeenAtUtc,
                session.IdleExpiresAtUtc, session.AbsoluteExpiresAtUtc, session.RevokedAtUtc,
                session.Id == actor.SessionId
            ));
    }
}
