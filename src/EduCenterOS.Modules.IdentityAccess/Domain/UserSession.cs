namespace EduCenterOS.Modules.IdentityAccess.Domain;

internal sealed class UserSession
{
    private UserSession()
    {
    }

    internal UserSession(Guid id, Guid accountId, long securityVersion, DateTimeOffset now, int idleSeconds, int absoluteSeconds)
    {
        RegistrationErrors.RequireUtc(now);

        if (
            id == Guid.Empty ||
            accountId == Guid.Empty ||
            securityVersion < 1 ||
            idleSeconds < 1 ||
            absoluteSeconds < idleSeconds
        )
            throw new ArgumentException("IdentityAccess.InvalidSession");

        Id = id;
        UserAccountId = accountId;
        SecurityVersionAtAuthentication = securityVersion;
        CreatedAtUtc = AuthenticatedAtUtc = LastPrimaryAuthenticatedAtUtc = LastSeenAtUtc = now;
        IdleExpiresAtUtc = now.AddSeconds(idleSeconds);
        AbsoluteExpiresAtUtc = now.AddSeconds(absoluteSeconds);
    }

    public Guid Id { get; private set; }
    public Guid UserAccountId { get; private set; }
    public long SecurityVersionAtAuthentication { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset AuthenticatedAtUtc { get; private set; }
    public DateTimeOffset LastPrimaryAuthenticatedAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }
    public DateTimeOffset IdleExpiresAtUtc { get; private set; }
    public DateTimeOffset AbsoluteExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevocationReason { get; private set; }
    public string AssuranceLevel { get; private set; } = "PrimaryAuthenticated";
    public long Version { get; private set; } = 1;

    internal DateTimeOffset EffectiveExpiration => IdleExpiresAtUtc < AbsoluteExpiresAtUtc ? IdleExpiresAtUtc : AbsoluteExpiresAtUtc;

    internal bool IsActive(DateTimeOffset now)
    {
        RegistrationErrors.RequireUtc(now);

        return RevokedAtUtc is null && now >= CreatedAtUtc && now < EffectiveExpiration;
    }

    internal void RefreshActivity(DateTimeOffset now, int idleSeconds)
    {
        if (!IsActive(now) || now < LastSeenAtUtc || idleSeconds < 1)
            throw new InvalidOperationException("IdentityAccess.SessionInactive");

        LastSeenAtUtc = now;
        var idle = now.AddSeconds(idleSeconds);
        IdleExpiresAtUtc = idle < AbsoluteExpiresAtUtc ? idle : AbsoluteExpiresAtUtc;
        Version++;
    }

    internal void Revoke(DateTimeOffset now, string reason)
    {
        RegistrationErrors.RequireUtc(now);

        if (now < CreatedAtUtc || reason is not ("LogoutCurrent" or "LogoutAll" or "UserRequest" or "RefreshReuse" or "SecurityReset"))
            throw new ArgumentException("IdentityAccess.InvalidRevocation");

        if (RevokedAtUtc is not null) return;

        RevokedAtUtc = now;
        RevocationReason = reason;
        Version++;
    }
}
