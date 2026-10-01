using EduCenterOS.Api.Runtime;
using EduCenterOS.Api.Options;
using EduCenterOS.Api.Health;

namespace EduCenterOS.Api.Configuration;

internal static class ApiServiceRegistration
{
    internal static void AddApiServices(this WebApplicationBuilder builder)
    {
        var environment = builder.Environment.EnvironmentName;
        var hostOptions = LocalHostOptions.BindSafely(builder.Configuration);
        builder.Services.AddOptions<LocalHostOptions>()
            .Configure(options => options.Port = hostOptions.Port)
            .Validate(options => options.Port is >= 1024 and <= 65535, "Platform:Host:Port must be between 1024 and 65535.")
            .ValidateOnStart();

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.None);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.None);
        builder.Logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks", LogLevel.None);
        builder.Logging.AddFilter("System.Net.Http", LogLevel.None);

        builder.Services.AddSingleton<IRuntimeSnapshotSource>(new EnvironmentSnapshotSource(environment));
        builder.Services.AddOptions<DatabaseProbeOptions>()
            .Configure<IRuntimeSnapshotSource>((options, source) => options.ConnectionString = source.Read().ConnectionString)
            .Validate(options => options.TimeoutSeconds is > 0 and <= 10, "Database probe timeout must be between 1 and 10 seconds.")
            .ValidateOnStart();
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
    }
}
