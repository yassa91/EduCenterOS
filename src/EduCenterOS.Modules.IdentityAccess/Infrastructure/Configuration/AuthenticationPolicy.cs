using System.Text.Json;
using System.Text.Json.Serialization;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Configuration;

internal sealed class AuthenticationPolicy
{
    public string Issuer { get; init; } = "https://localhost:5101";
    public string Audience { get; init; } = "educenteros-api";
    public string BrowserOrigin { get; init; } = "https://localhost:5101";
    public int AccessLifetimeSeconds { get; init; } = 600;
    public int IdleTimeoutSeconds { get; init; } = 2592000;
    public int AbsoluteLifetimeSeconds { get; init; } = 7776000;
    public int ClockSkewSeconds { get; init; } = 5;
    public int LockoutAttempts { get; init; } = 5;
    public int LockoutSeconds { get; init; } = 300;
    public int LoginWindowSeconds { get; init; } = 900;
    public int LoginIdentifierPermits { get; init; } = 10;
    public int SourceWindowSeconds { get; init; } = 60;
    public int SourcePermits { get; init; } = 60;
    public int RefreshSourcePermits { get; init; } = 30;

    internal static AuthenticationPolicy Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            string[] expected = ["issuer", "audience", "browserOrigin", "accessLifetimeSeconds", "idleTimeoutSeconds",
                "absoluteLifetimeSeconds", "clockSkewSeconds", "lockoutAttempts", "lockoutSeconds", "loginWindowSeconds",
                "loginIdentifierPermits", "sourceWindowSeconds", "sourcePermits", "refreshSourcePermits"];
            var fields = document.RootElement.EnumerateObject().Select(value => value.Name).ToArray();

            if (
                fields.Length != expected.Length || fields.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Length ||
                fields.Except(expected, StringComparer.Ordinal).Any()
            )
                throw new InvalidOperationException();

            var policy = JsonSerializer.Deserialize<AuthenticationPolicy>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? throw new InvalidOperationException();
            policy.Validate();

            return policy;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new InvalidOperationException("Configuration.InvalidAuthenticationPolicy");
        }
    }

    internal void Validate()
    {
        if (
            !Uri.TryCreate(BrowserOrigin, UriKind.Absolute, out var origin) ||
            origin.Scheme != "https" || origin.Host != "localhost" ||
            origin.Port is < 1024 or > 65535 || origin.UserInfo.Length != 0 ||
            origin.GetLeftPart(UriPartial.Authority) != BrowserOrigin ||
            !Uri.TryCreate(Issuer, UriKind.Absolute, out var issuer) || issuer.Scheme != "https" ||
            issuer.UserInfo.Length != 0 || issuer.Query.Length != 0 || issuer.Fragment.Length != 0 ||
            string.IsNullOrWhiteSpace(Audience) || Audience.Length > 100 ||
            Audience.Any(char.IsControl) || AccessLifetimeSeconds is < 30 or > 900 ||
            IdleTimeoutSeconds is < 300 or > 2592000 || AbsoluteLifetimeSeconds is < 300 or > 7776000 ||
            AccessLifetimeSeconds > IdleTimeoutSeconds || IdleTimeoutSeconds > AbsoluteLifetimeSeconds ||
            ClockSkewSeconds is < 0 or > 30 || LockoutAttempts is < 1 or > 20 ||
            LockoutSeconds is < 30 or > 3600 || LoginWindowSeconds is < 60 or > 3600 ||
            LoginIdentifierPermits is < 1 or > 20 || SourceWindowSeconds is < 10 or > 300 ||
            SourcePermits is < 1 or > 1000 || RefreshSourcePermits is < 1 or > 1000
        )
            throw new InvalidOperationException("Configuration.InvalidAuthenticationPolicy");
    }
}
