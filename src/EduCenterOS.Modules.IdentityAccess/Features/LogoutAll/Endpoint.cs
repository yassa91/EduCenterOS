using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EduCenterOS.Modules.IdentityAccess.Features.LogoutAll;

internal sealed class LogoutAllRequest
{
}

internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder routes, Func<Error, IResult> errorResult, Func<int, IResult> transportError)
    {
        routes.MapPost("/api/v1/auth/logout-all", (HttpContext context, LogoutAllHandler handler) =>
            StrictJsonBody.ReadAsync<LogoutAllRequest>(context, async request =>
            {
                var result = await handler.HandleAsync(context.RequestAborted);

                if (!result.IsSuccess) return errorResult(result.Error);

                AuthenticationCookie.Delete(context);

                return Results.NoContent();
            }, transportError))
            .RequireAuthorization("AccountSelf").WithTags("Authentication").Accepts<LogoutAllRequest>("application/json").Produces(204)
            .WithName("Authentication_LogoutAll").WithSummary("End all sessions existing at your account's command boundary")
            .WithDescription("AccountSelf; HTTPS/Origin/CSRF marker and empty object. Account then ordered session claims; actor rechecked after claims. SecurityVersion unchanged; a later Login is allowed.")
            .WithMetadata(new SecurityEndpointMetadata("AuthenticationSource", "AccountSelf", "SemanticallyIdempotent", true)).RequireRateLimiting("AuthenticationSource")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(406).ProducesProblem(413).ProducesProblem(415).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}
