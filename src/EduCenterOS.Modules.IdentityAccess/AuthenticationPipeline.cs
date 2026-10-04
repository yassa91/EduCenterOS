using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenterOS.Modules.IdentityAccess;

public static partial class ModuleRegistration
{
    public static IApplicationBuilder UseIdentityAccessBrowserSecurity(this IApplicationBuilder app, Func<HttpContext, Error, Task> writeError)
    {
        app.UseCors("AuthenticationBrowser");
        app.Use(async (context, next) =>
        {
            var metadata = context.GetEndpoint()?.Metadata.GetMetadata<SecurityEndpointMetadata>();

            if (metadata?.BrowserProtected == true)
            {
                var policy = context.RequestServices.GetRequiredService<AuthenticationRuntimeSettings>().Policy;
                var origins = context.Request.Headers.Origin;
                var marker = context.Request.Headers["X-EduCenterOS-Auth"];

                if (!context.Request.IsHttps || origins.Count != 1 || origins[0] != policy.BrowserOrigin || marker.Count != 1 || marker[0] != "1")
                {
                    await writeError(context, AuthenticationErrors.BrowserRejected);

                    return;
                }
            }

            await next(context);
        });

        return app;
    }
}
