using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EduCenterOS.Modules.IdentityAccess.Features.ListSessions;

internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder routes, Func<Error, IResult> errorResult, Func<int, IResult> transportError)
    {
        routes.MapGet("/api/v1/auth/sessions", async (HttpContext context, ListSessionsHandler handler) =>
        {
            if (StrictJsonBody.AcceptedRepresentation(context) is { } status) return transportError(status);

            var page = SessionRequestInputs.Page(context);

            if (!page.IsSuccess) return errorResult(page.Error);

            var result = await handler.HandleAsync(page.Value, context.RequestAborted);

            return result.IsSuccess ? Results.Json(result.Value) : errorResult(result.Error);
        })
        .RequireAuthorization("AccountSelf").WithTags("Authentication").Produces<SessionPageResponse>(200)
        .WithName("Authentication_ListSessions").WithSummary("List your sessions and retained history")
        .WithDescription("AccountSelf. Page>=1, pageSize 1–100 (defaults 1/20). Created DESC then ID DESC; count/page may drift during concurrent writes. No session activity writes.")
        .WithMetadata(new SecurityEndpointMetadata("AuthenticationSource", "AccountSelf", "NotRequiredRead")).RequireRateLimiting("AuthenticationSource")
        .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(406).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}
