using EduCenterOS.Api.Infrastructure;
using System.Net;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

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
builder.Services.AddOptions<LocalHostOptions>()
    .Configure(options => options.Port = hostOptions.Port)
    .Validate(options => options.Port is >= 1024 and <= 65535, "Platform:Host:Port must be between 1024 and 65535.")
    .ValidateOnStart();

builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, hostOptions.Port));

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

var app = builder.Build();
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ErrorBoundaryMiddleware>();
app.UseStatusCodePages(context => ApiProblems.WriteStatusAsync(context.HttpContext));

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponse.WriteAsync
}).WithMetadata(new HttpMethodMetadata(["GET", "HEAD"])).ExcludeFromDescription();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponse.WriteAsync
}).WithMetadata(new HttpMethodMetadata(["GET", "HEAD"])).ExcludeFromDescription();

app.Logger.LogInformation(new EventId(1000, "HostStarting"), "Starting API host in {Environment}.", environment);

app.Run();

public partial class Program;
