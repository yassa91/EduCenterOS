using EduCenterOS.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenterOS.Modules.IdentityAccess;

public static partial class ModuleRegistration
{
    private static void AddBrowserCors(IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<AuthenticationRuntimeSettings>((options, settings) =>
            options.AddPolicy("AuthenticationBrowser", policy => policy.WithOrigins(settings.Policy.BrowserOrigin)
                .WithMethods("GET", "POST").WithHeaders("Authorization", "Content-Type", "X-EduCenterOS-Auth").AllowCredentials()));
    }
}
