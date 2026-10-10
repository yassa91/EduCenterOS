namespace EduCenterOS.Modules.IdentityAccess.Features.VerifyPhone;

internal sealed class VerifyPhoneRequest
{
    public required string? Code { get; init; }
}

internal sealed class VerifyPhoneResponse(string verificationProof, DateTimeOffset expiresAtUtc)
{
    public string VerificationProof { get; } = verificationProof;
    public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
}
