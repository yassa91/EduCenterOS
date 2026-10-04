using EduCenterOS.Api.Runtime;
using EduCenterOS.Api.ErrorHandling;
using EduCenterOS.Modules.IdentityAccess;

namespace EduCenterOS.Api.Configuration;

internal static class ApplicationModuleRegistration
{
    internal static void MapApplicationModules(this WebApplication app)
    {
        app.MapIdentityAccess(ApiProblems.FromError, ApiProblems.FromStatus);
    }

    internal static void AddApplicationModules(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(provider => provider.GetRequiredService<IRuntimeSnapshotSource>().Read().IdentityAccess);
        builder.Services.AddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<IRuntimeSnapshotSource>().Read().Authentication;

            if (builder.Environment.IsDevelopment()) settings.ValidateDevelopmentHostPort(Options.LocalHostOptions.BindSafely(builder.Configuration).HttpsPort);

            return settings;
        });
        builder.Services.AddIdentityAccess(builder.Environment.EnvironmentName, ApiProblems.WriteErrorAsync);
    }
}
