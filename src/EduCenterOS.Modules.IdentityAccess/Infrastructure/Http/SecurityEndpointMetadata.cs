namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;

internal sealed record SecurityEndpointMetadata(string PolicyName)
{
    public string Classification => "AnonymousSecurity";
    public string IdempotencyMode => "SecuritySpecific";
}
