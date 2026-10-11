using EduCenterOS.Modules.IdentityAccess.Domain;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;

// Read-only authorization facts; no password, token hash, contact details, or tracked entity.
internal sealed class SessionAccessState
{
    internal required Guid AccountId { get; init; }
    internal required AccountStatus Status { get; init; }
    internal required DateTimeOffset AccountCreatedAtUtc { get; init; }
    internal required DateTimeOffset PhoneVerifiedAtUtc { get; init; }
    internal required long AccountSecurityVersion { get; init; }
    internal required Guid SessionId { get; init; }
    internal required Guid SessionAccountId { get; init; }
    internal required long SessionSecurityVersion { get; init; }
    internal required SessionLifetime Lifetime { get; init; }

    internal bool OwnsLiveSession(DateTimeOffset now) =>
        Status == AccountStatus.Active &&
        AccountCreatedAtUtc <= now &&
        PhoneVerifiedAtUtc <= now &&
        SessionAccountId == AccountId &&
        SessionSecurityVersion == AccountSecurityVersion &&
        Lifetime.IsActive(now);

    internal bool AcceptsActor(AuthenticatedActor actor, DateTimeOffset now) =>
        actor.AccountId == AccountId &&
        actor.SessionId == SessionId &&
        actor.SecurityVersion == AccountSecurityVersion &&
        OwnsLiveSession(now);

    internal static SessionAccessState From(UserAccount account, UserSession session) => new()
    {
        AccountId = account.Id,
        Status = account.Status,
        AccountCreatedAtUtc = account.CreatedAtUtc,
        PhoneVerifiedAtUtc = account.PhoneVerifiedAtUtc,
        AccountSecurityVersion = account.SecurityVersion,
        SessionId = session.Id,
        SessionAccountId = session.UserAccountId,
        SessionSecurityVersion = session.SecurityVersionAtAuthentication,
        Lifetime = session.Lifetime
    };
}
