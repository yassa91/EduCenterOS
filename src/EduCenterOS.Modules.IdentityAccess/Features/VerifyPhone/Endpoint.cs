using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Features.RequestPhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
namespace EduCenterOS.Modules.IdentityAccess.Features.VerifyPhone;

internal static class Endpoint
{
    internal static void Map(RouteGroupBuilder group, Func<Error,IResult> errorResult, Func<int,IResult> transportError)
    {
        group.MapPost("/{challengeId}/verify", (HttpContext context, VerifyPhoneHandler handler) =>
        {
            if (!StrictJsonBody.RouteId(context, out var id)) return Task.FromResult(transportError(400));
            return StrictJsonBody.ReadAsync<VerifyPhoneRequest>(context, async request =>
            {
                var result = await handler.HandleAsync(id, request.Code, context.RequestAborted);
                return result.IsSuccess ? Results.Ok(result.Value) : errorResult(result.Error);
            }, transportError);
        }).Accepts<VerifyPhoneRequest>("application/json").Produces<VerifyPhoneResponse>()
            .WithName("PhoneVerifications_Verify").WithSummary("Verify code and issue one temporary proof")
            .WithDescription("AnonymousSecurity. AnonymousIngress + OtpVerify. Proof secret is returned once; no account/login created.")
            .WithMetadata(new SecurityEndpointMetadata("OtpVerify")).RequireRateLimiting("OtpVerify")
            .ProducesProblem(400).ProducesProblem(406).ProducesProblem(413).ProducesProblem(415).ProducesProblem(422).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}
