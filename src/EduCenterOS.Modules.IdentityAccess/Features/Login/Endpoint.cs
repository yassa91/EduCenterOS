using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Features.Shared.Authentication;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EduCenterOS.Modules.IdentityAccess.Features.Login;

internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder routes, Func<Error, IResult> errorResult, Func<int, IResult> transportError)
    {
        routes.MapPost("/api/v1/auth/login", (HttpContext context, LoginHandler handler, IClock clock) =>
            StrictJsonBody.ReadAsync<LoginRequest>(context, async request =>
            {
                var result = await handler.HandleAsync(request, context.RequestAborted);

                if (!result.IsSuccess) return errorResult(result.Error);

                AuthenticationCookie.Set(context, result.Value.Refresh, result.Value.RefreshExpiresAtUtc, clock.UtcNow);

                return Results.Json(result.Value.Response);
            }, transportError))
            .AllowAnonymous().WithTags("Authentication").Accepts<LoginRequest>("application/json").Produces<AccessResponse>(200)
            .WithName("Authentication_Login").WithSummary("Sign in with verified phone and password")
            .WithDescription("HTTPS browser origin/header required. Generic account-neutral failures. Access stays in memory; refresh is a Secure HttpOnly SameSite Strict cookie only. Registration never logs in automatically.")
            .WithMetadata(new SecurityEndpointMetadata("AuthenticationSource", BrowserProtected: true)).RequireRateLimiting("AuthenticationSource")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(406).ProducesProblem(413).ProducesProblem(415).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}
