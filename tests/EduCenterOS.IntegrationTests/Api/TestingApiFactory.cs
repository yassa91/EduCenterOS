using System.Collections.Concurrent;
using System.Text.Json;
using EduCenterOS.Api.Infrastructure;
using EduCenterOS.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace EduCenterOS.IntegrationTests.Api;

internal sealed class TestingApiFactory(OwnedPostgresFixture database) : WebApplicationFactory<Program>
{
    internal SafeLogCapture Logs { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
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
            services.AddSingleton<IRuntimeSnapshotSource>(new SyntheticSnapshotSource(database));
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }

    private sealed class SyntheticSnapshotSource(OwnedPostgresFixture database) : IRuntimeSnapshotSource
    {
        private readonly RuntimeSnapshot snapshot = RuntimeSnapshot.Parse(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            environment = "Testing",
            source = "Synthetic",
            secrets = new Dictionary<string, string> { ["ConnectionStrings__RuntimeProbeDatabase"] = database.RuntimeConnectionString }
        }), "Testing");

        public RuntimeSnapshot Read() => snapshot;
    }
}

internal sealed class SafeLogCapture : ILoggerProvider
{
    internal ConcurrentQueue<(int EventId, string Message, Exception? Exception)> Events { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);
    public void Dispose() { }

    private sealed class CaptureLogger(SafeLogCapture owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Events.Enqueue((eventId.Id, formatter(state, exception), exception));
    }
}
