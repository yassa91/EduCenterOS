using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using Microsoft.IdentityModel.Tokens;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;

internal sealed class AuthenticationTokens : IDisposable
{
    private readonly AuthenticationRuntimeSettings settings;
    private readonly IClock clock;
    private readonly RSA signingKey;
    private readonly IReadOnlyDictionary<string, RsaSecurityKey> validationKeys;

    public AuthenticationTokens(AuthenticationRuntimeSettings settings, IClock clock)
    {
        this.settings = settings;
        this.clock = clock;
        signingKey = RSA.Create();
        signingKey.ImportFromPem(settings.PrivateKeyPem);
        validationKeys = settings.ValidationPublicKeys.ToDictionary(pair => pair.Key, pair =>
        {
            using var publicKey = RSA.Create();
            publicKey.ImportFromPem(pair.Value);

            return new RsaSecurityKey(publicKey.ExportParameters(false)) { KeyId = pair.Key, CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } };
        }, StringComparer.Ordinal);
    }

    internal TokenValidationParameters ValidationParameters() => new()
    {
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ValidateIssuerSigningKey = true,
        ValidateIssuer = true,
        ValidIssuer = settings.Policy.Issuer,
        ValidateAudience = true,
        ValidAudience = settings.Policy.Audience,
        IgnoreTrailingSlashWhenValidatingAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(settings.Policy.ClockSkewSeconds),
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        ValidTypes = ["educenteros-access+jwt"],
        TryAllIssuerSigningKeys = false,
        IssuerSigningKeyResolver = (_, _, kid, _) => kid is not null && validationKeys.TryGetValue(kid, out var key) ? [key] : [],
        // Uses the same injected clock as authoritative session time. Lifetime is always enforced.
        LifetimeValidator = (notBefore, expires, _, _) => notBefore is null && expires is not null &&
            new DateTimeOffset(DateTime.SpecifyKind(expires.Value, DateTimeKind.Utc)) > clock.UtcNow.AddSeconds(-settings.Policy.ClockSkewSeconds)
    };

    internal IssuedAccessToken Issue(UserSession session, DateTimeOffset now)
    {
        var expires = now.AddSeconds(settings.Policy.AccessLifetimeSeconds);

        if (expires > session.EffectiveExpiration) expires = session.EffectiveExpiration;

        var issuedSeconds = now.ToUnixTimeSeconds();
        var expirySeconds = expires.ToUnixTimeSeconds();

        if (!session.IsActive(now) || expirySeconds <= issuedSeconds) throw new InvalidOperationException("IdentityAccess.SessionInactive");

        // Provider caching must not retain references to an RSA owned by a disposed key-ring instance.
        var securityKey = new RsaSecurityKey(signingKey)
        {
            KeyId = settings.CurrentKeyId,
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        var header = new JwtHeader(new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256));
        header["typ"] = "educenteros-access+jwt";
        var payload = new JwtPayload
        {
            ["iss"] = settings.Policy.Issuer,
            ["aud"] = settings.Policy.Audience,
            ["sub"] = session.UserAccountId.ToString("D"),
            ["sid"] = session.Id.ToString("D"),
            ["jti"] = Guid.NewGuid().ToString("D"),
            ["iat"] = issuedSeconds,
            ["exp"] = expirySeconds,
            ["sv"] = session.SecurityVersionAtAuthentication
        };
        var value = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));

        return new IssuedAccessToken(value, DateTimeOffset.FromUnixTimeSeconds(expirySeconds));
    }

    // Profile inspection is called only after framework signature/lifetime validation.
    internal bool TryReadActor(string encodedToken, out AuthenticatedActor actor)
    {
        actor = default;

        try
        {
            var parts = encodedToken.Split('.');

            if (parts.Length != 3 || encodedToken.Length > 8192) return false;

            using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[0]));
            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
            string[] headerNames = ["alg", "kid", "typ"];
            string[] claimNames = ["iss", "aud", "sub", "sid", "jti", "iat", "exp", "sv"];

            if (!ExactKeys(header.RootElement, headerNames) || !ExactKeys(payload.RootElement, claimNames)) return false;

            var claims = payload.RootElement;

            if (
                header.RootElement.GetProperty("alg").GetString() != SecurityAlgorithms.RsaSha256 ||
                header.RootElement.GetProperty("typ").GetString() != "educenteros-access+jwt" ||
                !validationKeys.ContainsKey(header.RootElement.GetProperty("kid").GetString()!) ||
                claims.GetProperty("iss").GetString() != settings.Policy.Issuer ||
                claims.GetProperty("aud").GetString() != settings.Policy.Audience ||
                !ReadId(claims.GetProperty("sub"), out var accountId) ||
                !ReadId(claims.GetProperty("sid"), out var sessionId) ||
                !ReadId(claims.GetProperty("jti"), out _) ||
                !claims.GetProperty("iat").TryGetInt64(out var issued) ||
                !claims.GetProperty("exp").TryGetInt64(out var expires) ||
                !claims.GetProperty("sv").TryGetInt64(out var version) || version < 1 || issued < 0 || expires <= issued || expires > 253402300799 ||
                issued > clock.UtcNow.AddSeconds(settings.Policy.ClockSkewSeconds).ToUnixTimeSeconds() ||
                expires - issued > settings.Policy.AccessLifetimeSeconds
            )
                return false;

            actor = new AuthenticatedActor(accountId, sessionId, version);

            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    internal static string NewRefresh() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    internal static byte[] HashRefresh(string value) => SHA256.HashData(Encoding.ASCII.GetBytes(value));

    private static bool ReadId(JsonElement element, out Guid value)
    {
        value = Guid.Empty;

        return element.ValueKind == JsonValueKind.String && element.GetString() is { Length: 36 } text &&
            Guid.TryParseExact(text, "D", out value) && value != Guid.Empty && value.ToString("D") == text;
    }

    private static bool ExactKeys(JsonElement element, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;

        var names = element.EnumerateObject().Select(value => value.Name).ToArray();

        return names.Length == expected.Length && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length &&
            !names.Except(expected, StringComparer.Ordinal).Any();
    }

    public void Dispose() => signingKey.Dispose();
}

// These carriers have no generated secret-bearing ToString.
internal sealed class IssuedAccessToken(string value, DateTimeOffset expiresAtUtc)
{
    internal string Value { get; } = value;
    internal DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
}

internal readonly record struct AuthenticatedActor(Guid AccountId, Guid SessionId, long SecurityVersion);
