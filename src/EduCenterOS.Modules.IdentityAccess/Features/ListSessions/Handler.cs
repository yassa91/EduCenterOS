using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Features.ListSessions;

internal sealed record SessionItem(Guid SessionId, DateTimeOffset CreatedAtUtc, DateTimeOffset AuthenticatedAtUtc,
    DateTimeOffset LastSeenAtUtc, DateTimeOffset IdleExpiresAtUtc, DateTimeOffset AbsoluteExpiresAtUtc, DateTimeOffset? RevokedAtUtc, bool IsCurrent);

internal sealed record SessionPagination(string Type, int Page, int PageSize, long TotalCount, long TotalPages);

internal sealed record SessionPageResponse(IReadOnlyList<SessionItem> Items, SessionPagination Pagination);

internal sealed class ListSessionsHandler(ICurrentAccountActor current, IDbContextFactory<IdentityAccessDbContext> factory)
{
    internal async Task<Result<SessionPageResponse>> HandleAsync(SessionPage page, CancellationToken cancellationToken)
    {
        var actor = current.Require();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var count = await context.Sessions.AsNoTracking().LongCountAsync(session => session.UserAccountId == actor.AccountId, cancellationToken);
        var offset = (long)(page.Page - 1) * page.PageSize;
        var sessions = await context.Sessions.FromSqlInterpolated($"SELECT * FROM identity_access.user_sessions WHERE user_account_id={actor.AccountId} ORDER BY created_at_utc DESC,id DESC LIMIT {page.PageSize} OFFSET {offset}")
            .AsNoTracking().ToListAsync(cancellationToken);
        var items = sessions.Select(session => new SessionItem(session.Id, session.CreatedAtUtc, session.AuthenticatedAtUtc,
            session.LastSeenAtUtc, session.IdleExpiresAtUtc, session.AbsoluteExpiresAtUtc, session.RevokedAtUtc, session.Id == actor.SessionId)).ToArray();
        var pages = count / page.PageSize + (count % page.PageSize == 0 ? 0 : 1);

        return Result<SessionPageResponse>.Success(new SessionPageResponse(items, new SessionPagination("page", page.Page, page.PageSize, count, pages)));
    }
}
