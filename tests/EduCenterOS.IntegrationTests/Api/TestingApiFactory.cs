using EduCenterOS.BuildingBlocks.Time;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EduCenterOS.IntegrationTests.IdentityAccess;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Delivery;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using EduCenterOS.Api.Runtime;
using EduCenterOS.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace EduCenterOS.IntegrationTests.Api;

internal sealed class TestingApiFactory(OwnedPostgresFixture database, ControlledClock? clock = null, IInterceptor? interceptor = null, Action<IServiceCollection>? configureServices = null, Action<JsonObject>? authenticationPolicy = null) : WebApplicationFactory<Program>
{
    internal const string AuthenticationOrigin = "https://localhost:5443";
    internal SafeLogCapture Logs { get; } = new();
    internal TestOtpSender Sender { get; } = new();
    internal ControlledClock Clock { get; } = clock ?? new(DateTimeOffset.UtcNow);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        database.PrepareIdentityAsync().GetAwaiter().GetResult();
        // The factory serializes its host defaults as entry-point arguments. Clear that adapter
        // input: the application retains its own explicit environment/configuration validation.
        builder.ConfigureHostConfiguration(configuration => configuration.Sources.Clear());

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRuntimeSnapshotSource>();
            services.AddSingleton<IRuntimeSnapshotSource>(new CloudTestSnapshotSource(database, authenticationPolicy));
            services.AddSingleton<ILoggerProvider>(Logs);
            services.RemoveAll<IOtpSender>();
            services.AddSingleton<IOtpSender>(Sender);
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);

            if (interceptor is not null) services.AddSingleton<IInterceptor>(interceptor);

            configureServices?.Invoke(services);
        });
    }

    private sealed class TestSigningMaterial
    {
        internal string PrivatePem { get; }
        internal string PublicPem { get; }

        internal TestSigningMaterial()
        {
            using var key = RSA.Create(2048);
            PrivatePem = key.ExportPkcs8PrivateKeyPem();
            PublicPem = key.ExportSubjectPublicKeyInfoPem();
        }
    }

    private sealed class CloudTestSnapshotSource(OwnedPostgresFixture database, Action<JsonObject>? customizePolicy) : IRuntimeSnapshotSource
    {
        private static readonly TestSigningMaterial SigningKey = new();
        private readonly RuntimeSnapshot snapshot = RuntimeSnapshot.Parse(
            JsonSerializer.Serialize(new
            {
                schemaVersion = 4,
                environment = "Testing",
                databaseTarget = new { projectReference = database.Target.ProjectReference, host = database.Target.Host, environment = database.Target.Environment, serverMajor = database.Target.ServerMajor },
                source = "CloudTestFixture",
                securityPolicy = System.Text.Json.JsonSerializer.Deserialize<object>(File.ReadAllText(Path.Combine(OwnedPostgresFixture.FindRoot(), "infra/registration-policy.json"))),
                authenticationPolicy = TestAuthenticationPolicy(customizePolicy),
                developmentMailboxDirectory = (string?)null,
                secrets = new Dictionary<string, string>
                {
                    ["ConnectionStrings__RuntimeProbeDatabase"] = database.RuntimeConnectionString,
                    ["ConnectionStrings__IdentityAccessDatabase"] = database.ModuleConnectionString,
                    ["IdentityAccess__Jwt__PrivateKeyPem"] = SigningKey.PrivatePem,
                    ["IdentityAccess__Jwt__CurrentKeyId"] = "s03-test",
                    ["IdentityAccess__Jwt__ValidationPublicKeys__s03-test"] = SigningKey.PublicPem,
                    ["IdentityAccess__Otp__HashKeys__v1"] = Convert.ToBase64String(database.OtpKey),
                    ["IdentityAccess__Otp__CurrentHashKeyVersion"] = "v1",
                    ["Platform__RateLimiting__PartitionDigestKey"] = Convert.ToBase64String(database.PartitionKey)
                }
            }),
            "Testing"
        );

        private static JsonObject TestAuthenticationPolicy(Action<JsonObject>? customize)
        {
            var policy = JsonNode.Parse(File.ReadAllText(Path.Combine(OwnedPostgresFixture.FindRoot(), "infra/authentication-policy.json")))!.AsObject();
            policy["issuer"] = "https://educenteros.testing.invalid";
            policy["browserOrigin"] = AuthenticationOrigin;
            customize?.Invoke(policy);

            return policy;
        }

        public RuntimeSnapshot Read() => snapshot;
    }
}

internal sealed class SafeLogCapture : ILoggerProvider
{
    internal ConcurrentQueue<(int EventId, string Message, Exception? Exception)> Events { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);

    public void Dispose()
    {
    }

    private sealed class CaptureLogger(SafeLogCapture owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Events.Enqueue((eventId.Id, formatter(state, exception), exception));
    }
}
