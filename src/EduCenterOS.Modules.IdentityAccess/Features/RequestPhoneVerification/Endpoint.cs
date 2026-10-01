using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Features.RequestPhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
namespace EduCenterOS.Modules.IdentityAccess.Features.RequestPhoneVerification;

internal static class Endpoint
{
    internal static void Map(RouteGroupBuilder group, Func<Error,IResult> errorResult, Func<int,IResult> transportError)
    {
        group.MapPost("", (HttpContext context, RequestPhoneVerificationHandler handler) => StrictJsonBody.ReadAsync<PhoneVerificationRequest>(context, async request =>
        {
            var result = await handler.HandleAsync(request.PhoneNumber, context.RequestAborted);
            return result.IsSuccess ? Results.Ok(result.Value) : errorResult(result.Error);
        }, transportError)).Accepts<PhoneVerificationRequest>("application/json").Produces<PhoneVerificationResponse>()
            .WithName("RequestPhoneVerification").WithSummary("Request registration phone verification")
            .WithDescription("AnonymousSecurity. AnonymousIngress + OtpIssue. No code or account existence disclosure.")
            .WithMetadata(new SecurityEndpointMetadata("OtpIssue")).RequireRateLimiting("OtpIssue")
            .ProducesProblem(400).ProducesProblem(406).ProducesProblem(413).ProducesProblem(415).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}
