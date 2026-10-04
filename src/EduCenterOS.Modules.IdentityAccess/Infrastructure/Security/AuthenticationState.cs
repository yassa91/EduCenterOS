using EduCenterOS.Modules.IdentityAccess.Domain;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;

internal static class AuthenticationState
{
    internal static bool OwnsLiveSession(UserAccount account, UserSession session, DateTimeOffset now) =>
        account.Status == AccountStatus.Active &&
        account.CreatedAtUtc <= now &&
        account.PhoneVerifiedAtUtc <= now &&
        session.UserAccountId == account.Id &&
        session.SecurityVersionAtAuthentication == account.SecurityVersion &&
        session.IsActive(now);

    internal static bool AcceptsActor(UserAccount account, UserSession session, AuthenticatedActor actor, DateTimeOffset now) =>
        actor.AccountId == account.Id &&
        actor.SessionId == session.Id &&
        actor.SecurityVersion == account.SecurityVersion &&
        OwnsLiveSession(account, session, now);
}
