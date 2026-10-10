using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenterOS.Modules.IdentityAccess;

public static partial class ModuleRegistration
{
    public static IServiceCollection AddIdentityAccess(this IServiceCollection services, string environment, Func<HttpContext, Error, Task> writeError)
    {
        if (environment is not ("Development" or "Testing")) throw new InvalidOperationException("Environment.NotEnabled");

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<RegistrationCryptography>();
        services.AddSingleton<AuthenticationTokens>();
        AddAccountAuthentication(services, writeError);
        services.AddScoped<Features.GetCurrentAccount.GetCurrentAccountHandler>();
        AddBrowserCors(services);
        AddPersistence(services);
        services.AddScoped<RegistrationTransactions>();
        services.AddScoped<AuthenticationTransactions>();
        services.AddScoped<Features.Login.LoginHandler>();
        services.AddScoped<Features.Refresh.RefreshHandler>();
        services.AddScoped<Features.ListSessions.ListSessionsHandler>();
        services.AddScoped<Features.RevokeSession.RevokeSessionHandler>();
        services.AddScoped<Features.Logout.LogoutHandler>();
        services.AddScoped<Features.LogoutAll.LogoutAllHandler>();
        services.AddSingleton<LoginDummyPassword>();
        services.AddHostedService<AuthenticationBudgetCleanup>();
        AddPhoneVerification(services, environment);
        AddRateLimiting(services, writeError);

        return services;
    }
}
