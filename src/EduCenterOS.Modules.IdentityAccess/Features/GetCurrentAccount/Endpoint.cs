using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EduCenterOS.Modules.IdentityAccess.Features.GetCurrentAccount;

internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder routes, Func<Error, IResult> errorResult, Func<int, IResult> transportError)
    {
        routes.MapGet("/api/v1/accounts/me", async (HttpContext context, GetCurrentAccountHandler handler) =>
        {
            if (context.Request.QueryString.HasValue) return transportError(400);
            if (StrictJsonBody.AcceptedRepresentation(context) is { } status) return transportError(status);

            var result = await handler.HandleAsync(context.RequestAborted);

            return result.IsSuccess ? Results.Json(result.Value) : errorResult(result.Error);
        })
        .RequireAuthorization("AccountSelf").WithTags("Accounts").Produces<CurrentAccountResponse>(200)
        .WithName("Accounts_GetCurrent").WithSummary("Read your current account")
        .WithDescription("AccountSelf. Bearer signature, strict profile and authoritative account/session state validated on every request. No session sliding or institution access.")
        .WithMetadata(new SecurityEndpointMetadata("AuthenticationSource", "AccountSelf", "NotRequiredRead")).RequireRateLimiting("AuthenticationSource")
        .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(406).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}
