using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Features.RequestPhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EduCenterOS.Modules.IdentityAccess.Features.ResendPhoneVerification;

internal static class Endpoint
{
    internal static void Map(RouteGroupBuilder group, Func<Error, IResult> errorResult, Func<int, IResult> transportError)
    {
        group.MapPost("/{challengeId}/resend", (HttpContext context, ResendPhoneVerificationHandler handler) =>
        {
            if (!StrictJsonBody.RouteId(context, out var id)) return Task.FromResult(transportError(400));

            return StrictJsonBody.ReadAsync<ResendPhoneVerificationRequest>(context, async request =>
            {
                var result = await handler.HandleAsync(id, context.RequestAborted);

                return result.IsSuccess ? Results.Ok(result.Value) : errorResult(result.Error);
            }, transportError);
        }).Accepts<ResendPhoneVerificationRequest>("application/json").Produces<PhoneVerificationResponse>()
            .WithName("PhoneVerifications_Resend").WithSummary("Replace a registration verification challenge")
            .WithDescription("AnonymousSecurity. AnonymousIngress + OtpIssue. Replaces old code/proof; rolling quotas persist.")
            .WithMetadata(new SecurityEndpointMetadata("OtpIssue")).RequireRateLimiting("OtpIssue")
            .ProducesProblem(400).ProducesProblem(406).ProducesProblem(413).ProducesProblem(415).ProducesProblem(422).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}
