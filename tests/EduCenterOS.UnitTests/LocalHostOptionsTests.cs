using EduCenterOS.Api.Options;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EduCenterOS.UnitTests;

public sealed class LocalHostOptionsTests
{
    [Theory]
    [InlineData(1024)]
    [InlineData(5100)]
    [InlineData(65535)]
    public void BindSafely_ValidPort_ReturnsBoundValue(int port)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Platform:Host:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture) }).Build();
        Assert.Equal(port, LocalHostOptions.BindSafely(config).Port);
    }

    [Fact]
    public void BindSafely_NoSection_UsesReviewedDefault()
        => Assert.Equal(5100, LocalHostOptions.BindSafely(new ConfigurationBuilder().Build()).Port);

    [Theory]
    [InlineData("Port", "1023")]
    [InlineData("Port", "65536")]
    [InlineData("Port", "diagnostic-sensitive-marker")]
    [InlineData("Unreviewed", "diagnostic-sensitive-marker")]
    public void BindSafely_InvalidValueOrKey_DoesNotExposeInput(string key, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { [$"Platform:Host:{key}"] = value }).Build();
        var exception = Assert.Throws<InvalidOperationException>(() => LocalHostOptions.BindSafely(config));
        Assert.StartsWith("Configuration.InvalidHostSettings", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.False(exception.ToString().Contains("diagnostic-sensitive-marker", StringComparison.Ordinal));
    }
}
