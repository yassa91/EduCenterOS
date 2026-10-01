using System.Diagnostics;
using EduCenterOS.IntegrationTests.Infrastructure;
using Xunit;

namespace EduCenterOS.IntegrationTests.Api;

public sealed class StartupSafetyTests
{
    [Theory]
    [InlineData("missing", "Environment.Invalid")]
    [InlineData("conflict", "Environment.Conflict")]
    [InlineData("unknown", "Environment.Invalid")]
    [InlineData("deployment", "Environment.NotEnabled")]
    [InlineData("arguments", "Runtime.ArgumentsNotSupported")]
    [InlineData("implicit-secret", "Configuration.InvalidRuntimeSnapshot")]
    [InlineData("bad-snapshot", "Configuration.InvalidRuntimeSnapshot")]
    [InlineData("bad-port", "Configuration.InvalidHostSettings")]
    [InlineData("reserved-section", "Configuration.UnexpectedCriticalSection")]
    public async Task NativeStartup_UnreviewedOrIncompleteConfiguration_FailsWithoutDisclosure(string scenario, string expectedCode)
    {
        const string marker = "diagnostic-sensitive-marker";
        var root = OwnedPostgresFixture.FindRoot();
        var working = Path.Combine(Path.GetTempPath(), "educenteros-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(working);
        try
        {
            var config = scenario switch
            {
                "bad-port" => "{\"Platform\":{\"Host\":{\"Port\":\"" + marker + "\"}}}",
                "reserved-section" => "{\"ConnectionStrings\":{\"RuntimeProbeDatabase\":\"" + marker + "\"}}",
                _ => "{}"
            };
            await File.WriteAllTextAsync(Path.Combine(working, "appsettings.json"), config, TestContext.Current.CancellationToken);
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = working, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(root, "src/EduCenterOS.Api/bin/Release/net10.0/EduCenterOS.Api.dll"));
            start.Environment.Clear();
            foreach (var key in new[] { "PATH", "HOME", "USER", "TMPDIR", "DOTNET_ROOT" })
                if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
            if (scenario != "missing") start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
            if (scenario == "unknown") start.Environment["DOTNET_ENVIRONMENT"] = marker;
            if (scenario == "deployment") start.Environment["DOTNET_ENVIRONMENT"] = "Production";
            if (scenario == "conflict") start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            if (scenario == "implicit-secret") start.Environment["ConnectionStrings__RuntimeProbeDatabase"] = marker;
            if (scenario == "bad-snapshot") start.Environment["EDUCENTEROS_RUNTIME_SNAPSHOT"] = marker;
            if (scenario == "arguments") start.ArgumentList.Add("--unreviewed=" + marker);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("TestInfrastructure.ProcessMissing");
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            try
            {
                await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            var diagnostic = await stdout + await stderr;
            Assert.NotEqual(0, process.ExitCode);
            Assert.True(diagnostic.Contains(expectedCode, StringComparison.Ordinal), "Startup must fail for the expected policy reason.");
            Assert.False(diagnostic.Contains(marker, StringComparison.Ordinal), "Startup diagnostics must not expose arbitrary input.");
        }
        finally
        {
            Directory.Delete(working, recursive: true);
        }
    }
}
