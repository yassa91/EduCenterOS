using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Features.RequestPhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Features.ResendPhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Features.VerifyPhone;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
namespace EduCenterOS.Modules.IdentityAccess;

public static partial class ModuleRegistration
{
    public static void MapIdentityAccess(this IEndpointRouteBuilder routes, Func<Error,IResult> errorResult, Func<int,IResult> transportError)
    {
        var group = routes.MapGroup("/api/v1/phone-verifications").AllowAnonymous().WithTags("Phone verification");
        Features.RequestPhoneVerification.Endpoint.Map(group, errorResult, transportError);
        Features.ResendPhoneVerification.Endpoint.Map(group, errorResult, transportError);
        Features.VerifyPhone.Endpoint.Map(group, errorResult, transportError);
        Features.RegisterAccount.Endpoint.Map(routes, errorResult, transportError);
    }
}
