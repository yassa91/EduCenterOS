using EduCenterOS.Api.Runtime;
using Xunit;

namespace EduCenterOS.UnitTests;

public sealed class RuntimeEnvironmentTests
{
    [Theory]
    [InlineData("Development", null, "Development")]
    [InlineData(null, "Testing", "Testing")]
    [InlineData("Testing", "Testing", "Testing")]
    public void Resolve_CanonicalAgreedSelectors_ReturnsEnvironment(string? dotnet, string? aspnet, string expected)
        => WithSelectors(dotnet, aspnet, () => Assert.Equal(expected, RuntimeEnvironment.Resolve()));

    [Theory]
    [InlineData(null, null, "Environment.Invalid")]
    [InlineData("testing", null, "Environment.Invalid")]
    [InlineData("", null, "Environment.Invalid")]
    [InlineData("diagnostic-sensitive-marker", null, "Environment.Invalid")]
    [InlineData("Development", "Testing", "Environment.Conflict")]
    [InlineData("Production", null, "Environment.NotEnabled")]
    [InlineData(null, "Staging", "Environment.NotEnabled")]
    public void Resolve_InvalidSelectors_FailsWithSafeDiagnostic(string? dotnet, string? aspnet, string code)
        => WithSelectors(dotnet, aspnet, () =>
        {
            var exception = Assert.Throws<InvalidOperationException>(RuntimeEnvironment.Resolve);
            Assert.StartsWith(code, exception.Message);
            Assert.False(exception.ToString().Contains("diagnostic-sensitive-marker", StringComparison.Ordinal));
        });

    private static void WithSelectors(string? dotnet, string? aspnet, Action action)
    {
        var previousDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var previousAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", dotnet);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", aspnet);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousDotnet);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousAspnet);
        }
    }
}
