using EduCenterOS.BuildingBlocks.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EduCenterOS.Modules.IdentityAccess;

public static partial class ModuleRegistration
{
    public static void MapIdentityAccess(this IEndpointRouteBuilder routes, Func<Error, IResult> errorResult, Func<int, IResult> transportError)
    {
        var group = routes.MapGroup("/api/v1/phone-verifications").AllowAnonymous().WithTags("Phone verification");
        Features.RequestPhoneVerification.Endpoint.Map(group, errorResult, transportError);
        Features.ResendPhoneVerification.Endpoint.Map(group, errorResult, transportError);
        Features.VerifyPhone.Endpoint.Map(group, errorResult, transportError);
        Features.RegisterAccount.Endpoint.Map(routes, errorResult, transportError);
        Features.Login.Endpoint.Map(routes, errorResult, transportError);
        Features.Refresh.Endpoint.Map(routes, errorResult, transportError);
        Features.GetCurrentAccount.Endpoint.Map(routes, errorResult, transportError);
    }
}
