using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Features.Login;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EduCenterOS.Modules.IdentityAccess.Features.Refresh;

internal sealed class RefreshRequest
{
}

internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder routes, Func<Error, IResult> errorResult, Func<int, IResult> transportError)
    {
        routes.MapPost("/api/v1/auth/refresh", (HttpContext context, RefreshHandler handler, IClock clock) =>
            StrictJsonBody.ReadAsync<RefreshRequest>(context, async request =>
            {
                var result = await handler.HandleAsync(AuthenticationCookie.Read(context), context.RequestAborted);

                if (!result.IsSuccess) return errorResult(result.Error);

                AuthenticationCookie.Set(context, result.Value.Refresh, result.Value.RefreshExpiresAtUtc, clock.UtcNow);

                return Results.Json(result.Value.Response);
            }, transportError))
            .AllowAnonymous().WithTags("Authentication").Accepts<RefreshRequest>("application/json").Produces<AccessResponse>(200)
            .WithName("Authentication_Refresh").WithSummary("Rotate the browser refresh credential")
            .WithDescription("HTTPS/exact Origin/CSRF marker, empty JSON object and refresh cookie only. Single-flight: consumed-token reuse commits session revocation. Lost response after Commit requires a new Login; no cached credential replay.")
            .WithMetadata(new SecurityEndpointMetadata("RefreshSource", BrowserProtected: true)).RequireRateLimiting("RefreshSource")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(406).ProducesProblem(413).ProducesProblem(415).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}
