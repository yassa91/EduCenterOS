namespace EduCenterOS.Modules.IdentityAccess.Domain;

internal sealed class RefreshTokenRecord
{
    private RefreshTokenRecord()
    {
    }

    internal RefreshTokenRecord(Guid id, Guid sessionId, byte[] hash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        RegistrationErrors.RequireUtc(now);
        RegistrationErrors.RequireUtc(expiresAt);

        if (id == Guid.Empty || sessionId == Guid.Empty || hash.Length != 32 || expiresAt <= now)
            throw new ArgumentException("IdentityAccess.InvalidRefreshRecord");

        Id = id;
        UserSessionId = sessionId;
        TokenHash = hash.ToArray();
        CreatedAtUtc = now;
        ExpiresAtUtc = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid UserSessionId { get; private set; }
    public byte[] TokenHash { get; private set; } = [];
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public long Version { get; private set; } = 1;

    internal bool CanConsume(DateTimeOffset now)
    {
        RegistrationErrors.RequireUtc(now);

        return ConsumedAtUtc is null && RevokedAtUtc is null && now >= CreatedAtUtc && now < ExpiresAtUtc;
    }

    internal void Consume(DateTimeOffset now, Guid replacementId)
    {
        if (!CanConsume(now) || replacementId == Guid.Empty || replacementId == Id)
            throw new InvalidOperationException("IdentityAccess.RefreshRejected");

        ConsumedAtUtc = now;
        ReplacedByTokenId = replacementId;
        Version++;
    }
}
