using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.AspNetCore.Http;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;

internal static class AuthenticationCookie
{
    internal const string Name = "__Secure-educenteros-refresh";
    internal const string Path = "/api/v1/auth";

    internal static string? Read(HttpContext context)
    {
        // Do not let a cookie parser silently choose among duplicated credentials.
        var candidates = context.Request.Headers.Cookie.SelectMany(header => (header ?? string.Empty).Split(';'))
            .Select(value => value.Trim()).Where(value => value.StartsWith(Name + "=", StringComparison.Ordinal)).ToArray();

        if (candidates.Length != 1) return null;

        var raw = candidates[0][(Name.Length + 1)..];

        return RegistrationCryptography.IsProof(raw) ? raw : null;
    }

    internal static void Set(HttpContext context, string raw, DateTimeOffset expires, DateTimeOffset now)
    {
        var options = Options();
        options.Expires = DateTimeOffset.FromUnixTimeSeconds(expires.ToUnixTimeSeconds());
        options.MaxAge = TimeSpan.FromSeconds(Math.Floor((options.Expires.Value - now).TotalSeconds));

        if (!RegistrationCryptography.IsProof(raw) || options.MaxAge <= TimeSpan.Zero)
            throw new InvalidOperationException("IdentityAccess.InvalidCookieLifetime");

        context.Response.Cookies.Append(Name, raw, options);
    }

    internal static void Delete(HttpContext context) => context.Response.Cookies.Delete(Name, Options());

    private static CookieOptions Options() => new()
    {
        Path = Path,
        Secure = true,
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true
    };
}
