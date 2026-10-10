using System.Threading.RateLimiting;
using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenterOS.Modules.IdentityAccess;

public static partial class ModuleRegistration
{
    private static void AddRateLimiting(IServiceCollection services, Func<HttpContext, Error, Task> writeError)
    {
        services.AddRateLimiter(_ =>
        {
        });

        services.AddOptions<RateLimiterOptions>().Configure<IdentityAccessRuntimeSettings, RegistrationCryptography>((options, settings, crypto) =>
        {
            options.OnRejected = async (context, cancellation) =>
            {
                int? retry = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)) : null;
                await writeError(
                    context.HttpContext,
                    context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<SecurityEndpointMetadata>() is { BrowserProtected: true } or { Classification: "AccountSelf" }
                        ? AuthenticationErrors.Throttled(retry)
                        : new Error(
                            "Infrastructure.RateLimitExceeded",
                            ErrorCategory.RateLimited,
                            "The request limit has been reached.",
                            retry
                        )
                );
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, int>(context =>
                context.GetEndpoint()?.Metadata.GetMetadata<SecurityEndpointMetadata>() is null
                    ? RateLimitPartition.GetNoLimiter(-1)
                    : Partition(context, crypto, settings.Policy.AnonymousIpPermits, 60));
            options.AddPolicy("AnonymousIngress", context => Partition(context, crypto, settings.Policy.AnonymousIpPermits, 60));
            options.AddPolicy("OtpIssue", context => Partition(context, crypto, settings.Policy.IssueIpPermits, 900));
            options.AddPolicy("OtpVerify", context => Partition(context, crypto, settings.Policy.VerifyIpPermits, 60));
        });

        services.AddOptions<RateLimiterOptions>().Configure<AuthenticationRuntimeSettings, RegistrationCryptography>((options, settings, crypto) =>
        {
            options.AddPolicy("AuthenticationSource", context => Partition(context, crypto, settings.Policy.SourcePermits, settings.Policy.SourceWindowSeconds));
            options.AddPolicy("RefreshSource", context => Partition(context, crypto, settings.Policy.RefreshSourcePermits, settings.Policy.SourceWindowSeconds));
        });
    }

    private static RateLimitPartition<int> Partition(HttpContext context, RegistrationCryptography crypto, int permits, int window)
        => RateLimitPartition.GetSlidingWindowLimiter(crypto.Bucket(context.Connection.RemoteIpAddress?.ToString() ?? "missing"),
            _ => new SlidingWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromSeconds(window), SegmentsPerWindow = 4, QueueLimit = 0, AutoReplenishment = true });
}
