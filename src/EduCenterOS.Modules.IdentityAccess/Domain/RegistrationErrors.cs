using EduCenterOS.BuildingBlocks.Results;

namespace EduCenterOS.Modules.IdentityAccess.Domain;

internal static class RegistrationErrors
{
    internal static Error VerificationRejected => new(
        "IdentityAccess.VerificationRejected",
        ErrorCategory.BusinessRule,
        "Verification could not be completed."
    );

    internal static Error RegistrationRejected => new(
        "IdentityAccess.RegistrationRejected",
        ErrorCategory.Conflict,
        "Account registration could not be completed."
    );

    internal static Error RateLimited(int seconds) => new(
        "Infrastructure.RateLimitExceeded",
        ErrorCategory.RateLimited,
        "The request limit has been reached.",
        seconds
    );

    internal static Error Field(string member) => Error.Validation([new(
        member,
        "IdentityAccess.Input.Invalid",
        "The field is invalid."
    )]);

    internal static void RequireUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero) throw new ArgumentException("IdentityAccess.UtcRequired");
    }
}
