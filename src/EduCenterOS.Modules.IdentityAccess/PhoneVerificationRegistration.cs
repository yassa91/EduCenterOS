using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Features.RegisterAccount;
using EduCenterOS.Modules.IdentityAccess.Features.RequestPhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Features.ResendPhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Features.Shared.PhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Features.VerifyPhone;
using EduCenterOS.Modules.IdentityAccess.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Delivery;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EduCenterOS.Modules.IdentityAccess;

public static partial class ModuleRegistration
{
    private static void AddPhoneVerification(IServiceCollection services, string environment)
    {
        services.AddScoped<PhoneVerificationIssuance>();
        services.AddScoped<RequestPhoneVerificationHandler>();
        services.AddScoped<ResendPhoneVerificationHandler>();
        services.AddScoped<VerifyPhoneHandler>();
        services.AddScoped<RegisterAccountHandler>();
        services.AddOptions<PasswordHasherOptions>().Configure<IdentityAccessRuntimeSettings>((options, settings) =>
        {
            options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
            options.IterationCount = settings.Policy.PasswordIterations;
        });
        services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
        services.AddHostedService<RegistrationRuntimeGuard>();
        services.AddHostedService<RegistrationStateCleanup>();
        services.AddHealthChecks().AddCheck<IdentityDatabaseHealthCheck>("identity_access", tags: ["ready"]);

        if (environment == "Development")
        {
            services.AddSingleton(provider => new DevelopmentOtpSender(
                environment,
                provider.GetRequiredService<IdentityAccessRuntimeSettings>().MailboxDirectory!,
                provider.GetRequiredService<IClock>()
            ));
            services.AddSingleton<IOtpSender>(provider => provider.GetRequiredService<DevelopmentOtpSender>());
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<DevelopmentOtpSender>());
        }
        else services.AddSingleton<IOtpSender, UnavailableOtpSender>();
    }
}
