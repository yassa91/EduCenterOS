namespace EduCenterOS.Modules.IdentityAccess.Features.Login;

internal sealed class LoginRequest
{
    public required string? PhoneNumber { get; init; }
    public required string? Password { get; init; }
}
