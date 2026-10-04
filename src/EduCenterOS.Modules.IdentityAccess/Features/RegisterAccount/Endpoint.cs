using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EduCenterOS.Modules.IdentityAccess.Features.RegisterAccount;

internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder routes, Func<Error, IResult> errorResult, Func<int, IResult> transportError)
    {
        routes.MapPost("/api/v1/accounts", (HttpContext context, RegisterAccountHandler handler) =>
            StrictJsonBody.ReadAsync<RegisterAccountRequest>(context, async request =>
            {
                var result = await handler.HandleAsync(request, context.RequestAborted);

                return result.IsSuccess ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created) : errorResult(result.Error);
            }, transportError))
            .AllowAnonymous().WithTags("Accounts").Accepts<RegisterAccountRequest>("application/json").Produces<RegisterAccountResponse>(201)
            .WithName("Accounts_Register").WithSummary("Create an account using a verified phone proof")
            .WithDescription("AnonymousSecurity. AnonymousIngress. Consumes the proof once; no login, session or institution access is granted.")
            .WithMetadata(new SecurityEndpointMetadata("AnonymousIngress"))
            .ProducesProblem(400).ProducesProblem(406).ProducesProblem(409).ProducesProblem(413).ProducesProblem(415).ProducesProblem(422).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}
