using EduCenterOS.BuildingBlocks.Results;

namespace EduCenterOS.Modules.IdentityAccess.Domain;

internal static class AuthenticationErrors
{
    internal static Error Rejected => new(
        "IdentityAccess.AuthenticationRejected",
        ErrorCategory.Authentication,
        "Authentication could not be completed."
    );

    internal static Error BrowserRejected => new(
        "IdentityAccess.BrowserRequestRejected",
        ErrorCategory.Authorization,
        "The browser request could not be accepted."
    );

    internal static Error SessionNotFound => new(
        "IdentityAccess.SessionNotFound",
        ErrorCategory.NotFound,
        "The session was not found."
    );

    internal static Error Throttled(int? delay) => new(
        "IdentityAccess.AuthenticationThrottled",
        ErrorCategory.RateLimited,
        "The request limit has been reached.",
        delay
    );
}
