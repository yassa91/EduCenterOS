using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EduCenterOS.Modules.IdentityAccess.Features.RevokeSession;

internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder routes, Func<Error, IResult> errorResult, Func<int, IResult> transportError)
    {
        routes.MapPost("/api/v1/auth/sessions/{sessionId}/revoke", (HttpContext context, RevokeSessionHandler handler) =>
            StrictJsonBody.ReadAsync<RevokeSessionRequest>(context, async request =>
            {
                var id = SessionRequestInputs.SessionId(context);

                if (!id.IsSuccess) return errorResult(id.Error);

                var result = await handler.HandleAsync(id.Value, AuthenticationCookie.Read(context), context.RequestAborted);

                if (!result.IsSuccess) return errorResult(result.Error);
                if (result.Value) AuthenticationCookie.Delete(context);

                return Results.NoContent();
            }, transportError))
            .RequireAuthorization("AccountSelf").WithTags("Authentication").Accepts<RevokeSessionRequest>("application/json").Produces(204)
            .WithName("Authentication_RevokeSession").WithSummary("Revoke one of your sessions")
            .WithDescription("AccountSelf; HTTPS/Origin/CSRF marker and empty object. Missing/foreign target same404; already-revoked owned target no-op. Matching presented refresh cookie is deleted after Commit.")
            .WithMetadata(new SecurityEndpointMetadata("AuthenticationSource", "AccountSelf", "SemanticallyIdempotent", true)).RequireRateLimiting("AuthenticationSource")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(406).ProducesProblem(413).ProducesProblem(415).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}
