using System.Text.Json;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Delivery;
using Xunit;
namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class DevelopmentMailboxTests
{
    private static string CanonicalTemporaryDirectory()
    {
        var path = Path.GetTempPath(); var current = Path.GetPathRoot(path)!;
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = new DirectoryInfo(Path.Combine(current, segment));
            current = next.LinkTarget is null ? next.FullName : next.ResolveLinkTarget(true)!.FullName;
        }
        return current;
    }
    [Theory]
    [InlineData("Testing")] [InlineData("Staging")] [InlineData("Production")]
    public void NonDevelopmentEnvironment_CannotCreateMailbox(string environment)
        => Assert.Throws<InvalidOperationException>(() => new DevelopmentOtpSender(environment, Path.GetTempPath(), new SystemClock()));
    [Fact]
    public async Task Mailbox_HasOwnerPermissionsAndLifecycleCleanupWithoutHttpExposure()
    {
        if (OperatingSystem.IsWindows()) throw new InvalidOperationException("TestInfrastructure.UnixRequired");
        var root = Path.Combine(CanonicalTemporaryDirectory(), "educenteros-mailbox-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, ".local/otp"); var clock = new ControlledClock(new DateTimeOffset(2026,10,1,12,0,0,TimeSpan.Zero));
        using var sender = new DevelopmentOtpSender("Development", directory, clock);
        try
        {
            await sender.StartAsync(TestContext.Current.CancellationToken);
            var id = Guid.CreateVersion7(); var path = Path.Combine(directory, id.ToString("D") + ".json");
            await sender.DeliverAsync(new OtpMessage(id, "827415", clock.UtcNow.AddSeconds(300)), TestContext.Current.CancellationToken);
            Assert.Equal(UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
            Assert.Equal(UnixFileMode.UserRead|UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)))
            { Assert.Equal(3, document.RootElement.EnumerateObject().Count()); Assert.True(document.RootElement.GetProperty("code").GetString() == "827415"); }
            using var second = new DevelopmentOtpSender("Development", directory, clock);
            await Assert.ThrowsAsync<InvalidOperationException>(() => second.StartAsync(TestContext.Current.CancellationToken));
            Assert.True(File.Exists(path));
            await sender.RemoveAsync(id, TestContext.Current.CancellationToken); Assert.False(File.Exists(path));
            var expired = Guid.CreateVersion7(); var expiredPath = Path.Combine(directory, expired.ToString("D") + ".json");
            await sender.DeliverAsync(new OtpMessage(expired, "827415", clock.UtcNow.AddSeconds(60)), TestContext.Current.CancellationToken);
            clock.UtcNow = clock.UtcNow.AddSeconds(60); sender.Sweep(clock.UtcNow); Assert.False(File.Exists(expiredPath));
            var pending = Guid.CreateVersion7(); await sender.DeliverAsync(new OtpMessage(pending, "827415", clock.UtcNow.AddSeconds(300)), TestContext.Current.CancellationToken);
            await sender.StopAsync(TestContext.Current.CancellationToken);
            Assert.Empty(Directory.GetFiles(directory, "*.json"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task SymbolicLinks_CannotRedirectSecretFiles()
    {
        if (OperatingSystem.IsWindows()) throw new InvalidOperationException("TestInfrastructure.UnixRequired");
        var root = Path.Combine(CanonicalTemporaryDirectory(), "educenteros-mailbox-links-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, ".local/otp"); var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(root); await File.WriteAllTextAsync(outside,"unchanged",TestContext.Current.CancellationToken);
        using var sender = new DevelopmentOtpSender("Development",directory,new SystemClock());
        try
        {
            await sender.StartAsync(TestContext.Current.CancellationToken); var id=Guid.CreateVersion7();
            var path=Path.Combine(directory,id.ToString("D")+".json"); File.CreateSymbolicLink(path,outside);
            await Assert.ThrowsAsync<OtpDeliveryException>(() => sender.DeliverAsync(new OtpMessage(id,"827415",DateTimeOffset.UtcNow.AddMinutes(5)),TestContext.Current.CancellationToken));
            Assert.Equal("unchanged",await File.ReadAllTextAsync(outside,TestContext.Current.CancellationToken)); File.Delete(path);
            await sender.StopAsync(TestContext.Current.CancellationToken);
            Directory.Delete(directory,true); Directory.CreateSymbolicLink(directory,root);
            using var linked = new DevelopmentOtpSender("Development",directory,new SystemClock());
            await Assert.ThrowsAsync<InvalidOperationException>(() => linked.StartAsync(TestContext.Current.CancellationToken));
            Directory.Delete(directory);
        }
        finally { Directory.Delete(root,true); }
    }
}
internal sealed class ControlledClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
