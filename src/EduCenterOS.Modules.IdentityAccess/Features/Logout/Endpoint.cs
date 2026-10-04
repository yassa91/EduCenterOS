using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EduCenterOS.Modules.IdentityAccess.Features.Logout;

internal sealed class LogoutRequest
{
}

internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder routes, Func<Error, IResult> errorResult, Func<int, IResult> transportError)
    {
        routes.MapPost("/api/v1/auth/logout", (HttpContext context, LogoutHandler handler) =>
            StrictJsonBody.ReadAsync<LogoutRequest>(context, async request =>
            {
                var result = await handler.HandleAsync(AuthenticationCookie.Read(context), context.RequestAborted);

                if (!result.IsSuccess) return errorResult(result.Error);

                AuthenticationCookie.Delete(context);

                return Results.NoContent();
            }, transportError))
            .AllowAnonymous().WithTags("Authentication").Accepts<LogoutRequest>("application/json").Produces(204)
            .WithName("Authentication_Logout").WithSummary("End the session identified by your refresh cookie")
            .WithDescription("HTTPS/Origin/CSRF marker and empty object. Does not require live access JWT. Missing/unresolvable cookie no-op with matching deletion; DB failure is not logout success.")
            .WithMetadata(new SecurityEndpointMetadata("AuthenticationSource", "AnonymousSecurity", "SemanticallyIdempotent", true)).RequireRateLimiting("AuthenticationSource")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(406).ProducesProblem(413).ProducesProblem(415).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}
