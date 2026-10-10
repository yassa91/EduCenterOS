using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;

namespace EduCenterOS.Modules.IdentityAccess.Features.Shared.Authentication;

internal sealed class AccessResponse
{
    public required string AccessToken { get; init; }
    public string TokenType { get; init; } = "Bearer";
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}

internal sealed class AuthenticationGrant(IssuedAccessToken access, string refresh, DateTimeOffset refreshExpiresAtUtc)
{
    internal AccessResponse Response { get; } = new() { AccessToken = access.Value, ExpiresAtUtc = access.ExpiresAtUtc };
    internal string Refresh { get; } = refresh;
    internal DateTimeOffset RefreshExpiresAtUtc { get; } = refreshExpiresAtUtc;
}
