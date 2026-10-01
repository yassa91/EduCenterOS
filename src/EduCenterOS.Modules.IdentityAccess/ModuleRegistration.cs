using Microsoft.AspNetCore.Builder;
using System.Threading.RateLimiting;
using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Delivery;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace EduCenterOS.Modules.IdentityAccess;

public static partial class ModuleRegistration
{
    public static IServiceCollection AddIdentityAccess(this IServiceCollection services, string environment, Func<HttpContext, Error, Task> writeError)
    {
        if (environment is not ("Development" or "Testing")) throw new InvalidOperationException("Environment.NotEnabled");
        services.AddSingleton<IClock, SystemClock>(); services.AddSingleton<RegistrationCryptography>();
        services.AddDbContextFactory<IdentityAccessDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IdentityAccessRuntimeSettings>().ConnectionString,
                postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "identity_access").CommandTimeout(5)));
        services.AddHostedService<RegistrationRuntimeGuard>();
        services.AddHostedService<RegistrationStateCleanup>();
        services.AddHealthChecks().AddCheck<IdentityDatabaseHealthCheck>("identity_access", tags: ["ready"]);
        if (environment == "Development")
        {
            services.AddSingleton(provider => new DevelopmentOtpSender(environment,
                provider.GetRequiredService<IdentityAccessRuntimeSettings>().MailboxDirectory!, provider.GetRequiredService<IClock>()));
            services.AddSingleton<IOtpSender>(provider => provider.GetRequiredService<DevelopmentOtpSender>());
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<DevelopmentOtpSender>());
        }
        else services.AddSingleton<IOtpSender, UnavailableOtpSender>();
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IdentityAccessRuntimeSettings, RegistrationCryptography>((options, settings, crypto) =>
        {
            options.OnRejected = async (context, cancellation) =>
            {
                int? retry = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)) : null;
                await writeError(context.HttpContext, new Error("Infrastructure.RateLimitExceeded", ErrorCategory.RateLimited, "The request limit has been reached.", retry));
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, int>(context =>
                context.GetEndpoint()?.Metadata.GetMetadata<SecurityEndpointMetadata>() is null
                    ? RateLimitPartition.GetNoLimiter(-1)
                    : Partition(context, crypto, settings.Policy.AnonymousIpPermits, 60));
            options.AddPolicy("AnonymousIngress", context => Partition(context, crypto, settings.Policy.AnonymousIpPermits, 60));
            options.AddPolicy("OtpIssue", context => Partition(context, crypto, settings.Policy.IssueIpPermits, 900));
            options.AddPolicy("OtpVerify", context => Partition(context, crypto, settings.Policy.VerifyIpPermits, 60));
        });
        return services;
    }
    private static RateLimitPartition<int> Partition(HttpContext context, RegistrationCryptography crypto, int permits, int window)
        => RateLimitPartition.GetSlidingWindowLimiter(crypto.Bucket(context.Connection.RemoteIpAddress?.ToString() ?? "missing"),
            _ => new SlidingWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromSeconds(window), SegmentsPerWindow = 4, QueueLimit = 0, AutoReplenishment = true });
}
