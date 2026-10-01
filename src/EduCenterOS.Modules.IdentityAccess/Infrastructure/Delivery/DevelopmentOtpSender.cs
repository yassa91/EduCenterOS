using System.Text.Json;
using EduCenterOS.BuildingBlocks.Time;
using Microsoft.Extensions.Hosting;
namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Delivery;

internal sealed class DevelopmentOtpSender : BackgroundService, IOtpSender
{
    private readonly string directory;
    private readonly IClock clock;
    private FileStream? lease;
    private readonly SemaphoreSlim mutex = new(1, 1);
    internal DevelopmentOtpSender(string environment, string directory, IClock clock)
    {
        if (environment != "Development" || OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("Delivery.DevelopmentOnly");
        this.directory = directory; this.clock = clock;
    }
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows()) throw new InvalidOperationException("Delivery.DevelopmentOnly");
        try
        {
            RejectLinks(directory);
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            foreach (var owned in new[] { Path.GetDirectoryName(directory)!, directory })
                File.SetUnixFileMode(owned, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var leasePath = Path.Combine(directory, ".owner"); RejectLinks(leasePath);
            lease = new FileStream(leasePath, new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite,
                Share = FileShare.None, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
            File.SetUnixFileMode(leasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Sweep(clock.UtcNow);
            return base.StartAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { lease?.Dispose(); lease = null; throw new InvalidOperationException("Delivery.MailboxUnavailable"); }
    }
    public async Task DeliverAsync(OtpMessage message, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows()) throw new OtpDeliveryException();
        await mutex.WaitAsync(cancellationToken);
        try
        {
            if (lease is null || message.ExpiresAtUtc <= clock.UtcNow) throw new OtpDeliveryException();
            var path = MessagePath(message.ChallengeId); RejectLinks(path);
            await using var file = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
                Share = FileShare.None, Options = FileOptions.Asynchronous, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
            await JsonSerializer.SerializeAsync(file, message, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }, cancellationToken);
            await file.FlushAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { throw new OtpDeliveryException(); }
        finally { mutex.Release(); }
    }
    public async Task RemoveAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        await mutex.WaitAsync(cancellationToken);
        try { var path = MessagePath(challengeId); RejectLinks(path); File.Delete(path); }
        finally { mutex.Release(); }
    }
    internal void Sweep(DateTimeOffset now)
    {
        if (OperatingSystem.IsWindows()) throw new InvalidOperationException("Delivery.DevelopmentOnly");
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "D", out _)) continue;
            RejectLinks(path);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            try
            {
                if (new FileInfo(path).Length > 1024) throw new JsonException();
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset() <= now) File.Delete(path);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            { File.Delete(path); }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await mutex.WaitAsync(stoppingToken);
            try { Sweep(clock.UtcNow); }
            finally { mutex.Release(); }
        }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await mutex.WaitAsync(cancellationToken);
        try
        {
            if (lease is not null)
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
                    if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "D", out _)) { RejectLinks(path); File.Delete(path); }
                lease.Dispose(); lease = null;
            }
        }
        finally { mutex.Release(); }
    }
    public override void Dispose() { lease?.Dispose(); lease = null; base.Dispose(); }
    private string MessagePath(Guid id) => Path.Combine(directory, id.ToString("D") + ".json");
    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (new FileInfo(current).LinkTarget is not null || ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
                throw new IOException("Delivery.LinkRejected");
    }
}
