namespace EduCenterOS.Modules.IdentityAccess.Features.Shared.PhoneVerification;

internal sealed class PhoneVerificationResponse(Guid challengeId, DateTimeOffset expiresAtUtc, DateTimeOffset resendAvailableAtUtc)
{
    public Guid ChallengeId { get; } = challengeId;
    public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
    public DateTimeOffset ResendAvailableAtUtc { get; } = resendAvailableAtUtc;
}
