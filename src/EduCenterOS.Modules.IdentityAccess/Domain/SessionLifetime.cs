namespace EduCenterOS.Modules.IdentityAccess.Domain;

internal readonly record struct SessionLifetime(
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset IdleExpiresAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc
)
{
    internal DateTimeOffset EffectiveExpiration =>
        IdleExpiresAtUtc < AbsoluteExpiresAtUtc ? IdleExpiresAtUtc : AbsoluteExpiresAtUtc;

    internal bool IsActive(DateTimeOffset now)
    {
        RegistrationErrors.RequireUtc(now);

        return RevokedAtUtc is null && now >= CreatedAtUtc && now < EffectiveExpiration;
    }
}
