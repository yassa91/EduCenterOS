namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;

internal sealed record SecurityEndpointMetadata(
    string PolicyName,
    string Classification = "AnonymousSecurity",
    string IdempotencyMode = "SecuritySpecific",
    bool BrowserProtected = false
);
