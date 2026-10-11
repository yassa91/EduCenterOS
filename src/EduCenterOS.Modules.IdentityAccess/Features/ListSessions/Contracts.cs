namespace EduCenterOS.Modules.IdentityAccess.Features.ListSessions;

internal sealed record SessionItem(
    Guid SessionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset AuthenticatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset IdleExpiresAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    bool IsCurrent
);

internal sealed record SessionPagination(string Type, int Page, int PageSize, long TotalCount, long TotalPages);

internal sealed record SessionPageResponse(IReadOnlyList<SessionItem> Items, SessionPagination Pagination);
