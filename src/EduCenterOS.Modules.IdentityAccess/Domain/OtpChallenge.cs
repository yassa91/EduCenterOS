using EduCenterOS.BuildingBlocks.Results;

namespace EduCenterOS.Modules.IdentityAccess.Domain;

internal enum ChallengeStatus
{
    Active,
    Verified,
    Consumed,
    Invalidated,
    Locked
}

internal sealed class OtpChallenge
{
    private OtpChallenge()
    {
    }

    internal OtpChallenge(Guid id, string target, byte[] hash, string keyVersion, DateTimeOffset now, int lifetimeSeconds)
    {
        RegistrationErrors.RequireUtc(now);
        var phone = RegistrationInputs.Phone(target);
        if (
            id == Guid.Empty || !phone.IsSuccess || phone.Value != target ||
            hash.Length != 32 || string.IsNullOrWhiteSpace(keyVersion) ||
            keyVersion.Length > 32 || lifetimeSeconds is < 60 or > 600
        )
        {
            throw new ArgumentException("IdentityAccess.InvalidChallenge");
        }

        Id = id;
        NormalizedTarget = target;
        CodeHash = hash.ToArray();
        HashKeyVersion = keyVersion;
        CreatedAtUtc = now;
        ExpiresAtUtc = now.AddSeconds(lifetimeSeconds);
    }

    public Guid Id { get; private set; }
    public string Purpose { get; private set; } = "RegisterAccount";
    public string NormalizedTarget { get; private set; } = string.Empty;
    public byte[] CodeHash { get; private set; } = [];
    public string HashKeyVersion { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public ChallengeStatus Status { get; private set; }
    public int FailedAttempts { get; private set; }
    public byte[]? ProofHash { get; private set; }
    public DateTimeOffset? VerifiedAtUtc { get; private set; }
    public DateTimeOffset? ProofExpiresAtUtc { get; private set; }

    internal bool CanVerify(DateTimeOffset now)
    {
        RegistrationErrors.RequireUtc(now);
        return Purpose == "RegisterAccount" && Status == ChallengeStatus.Active && now < ExpiresAtUtc;
    }

    internal Result FailAttempt(DateTimeOffset now, int maximum)
    {
        if (maximum is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(maximum));

        if (!CanVerify(now)) return Result.Failure(RegistrationErrors.VerificationRejected);

        FailedAttempts++;

        if (FailedAttempts >= maximum) Status = ChallengeStatus.Locked;

        return Result.Failure(RegistrationErrors.VerificationRejected);
    }

    internal Result Verify(DateTimeOffset now, byte[] proofHash, int lifetimeSeconds)
    {
        if (!CanVerify(now)) return Result.Failure(RegistrationErrors.VerificationRejected);

        if (proofHash.Length != 32 || lifetimeSeconds is < 60 or > 600) throw new ArgumentException("IdentityAccess.InvalidProof");

        Status = ChallengeStatus.Verified;
        ProofHash = proofHash.ToArray();
        VerifiedAtUtc = now;
        ProofExpiresAtUtc = now.AddSeconds(lifetimeSeconds);

        return Result.Success();
    }

    internal bool CanConsume(DateTimeOffset now)
    {
        RegistrationErrors.RequireUtc(now);

        return Purpose == "RegisterAccount" && Status == ChallengeStatus.Verified && ProofHash is not null && now < ProofExpiresAtUtc;
    }

    internal Result Consume(DateTimeOffset now)
    {
        if (!CanConsume(now)) return Result.Failure(RegistrationErrors.VerificationRejected);

        Status = ChallengeStatus.Consumed;
        ProofHash = null;

        return Result.Success();
    }

    internal void Invalidate()
    {
        if (Status is ChallengeStatus.Active or ChallengeStatus.Verified)
        {
            Status = ChallengeStatus.Invalidated;
            ProofHash = null;
        }
    }
}
