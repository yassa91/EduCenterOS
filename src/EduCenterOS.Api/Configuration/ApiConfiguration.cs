using System.Net;
using EduCenterOS.Api.Runtime;
using EduCenterOS.Api.Options;

namespace EduCenterOS.Api.Configuration;

internal static class ApiConfiguration
{
    internal static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var environment = RuntimeEnvironment.Resolve();

        if (args.Length != 0)
            throw new InvalidOperationException("Runtime.ArgumentsNotSupported: use the reviewed startup configuration.");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            EnvironmentName = environment
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false);
        var reservedSections = new[] { "ConnectionStrings", "IdentityAccess", "Platform:DataProtection", "Platform:RateLimiting" };

        if (reservedSections.Any(section => builder.Configuration.GetSection(section).Exists()))
            throw new InvalidOperationException("Configuration.UnexpectedCriticalSection: runtime secrets require an explicit bootstrap source.");

        var hostOptions = LocalHostOptions.BindSafely(builder.Configuration);
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, hostOptions.Port));

        return builder;
    }
}
