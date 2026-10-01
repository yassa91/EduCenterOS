using EduCenterOS.Api.Runtime;
using EduCenterOS.Api.ErrorHandling;
using EduCenterOS.Modules.IdentityAccess;
namespace EduCenterOS.Api.Configuration;

internal static class ApplicationModuleRegistration
{
    internal static void AddApplicationModules(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(provider => provider.GetRequiredService<IRuntimeSnapshotSource>().Read().IdentityAccess);
        builder.Services.AddIdentityAccess(builder.Environment.EnvironmentName, ApiProblems.WriteErrorAsync);
    }
}
