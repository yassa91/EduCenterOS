namespace EduCenterOS.Modules.IdentityAccess.Features.RegisterAccount;

internal sealed class RegisterAccountRequest
{
    public required Guid ChallengeId { get; init; }
    public required string? VerificationProof { get; init; }
    public required string? FullName { get; init; }
    public required string? Password { get; init; }
    public string? EmailAddress { get; init; }
}

internal sealed record RegisterAccountResponse(Guid UserAccountId, Guid PersonIdentityId, DateTimeOffset CreatedAtUtc);
