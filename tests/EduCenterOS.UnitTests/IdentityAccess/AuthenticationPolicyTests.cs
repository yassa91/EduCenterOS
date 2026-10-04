using System.Text.Json;
using System.Text.Json.Nodes;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Configuration;
using Xunit;

namespace EduCenterOS.UnitTests.IdentityAccess;

public sealed class AuthenticationPolicyTests
{
    [Theory]
    [InlineData("browserOrigin", "http://localhost:5101")]
    [InlineData("browserOrigin", "https://localhost:5101/")]
    [InlineData("browserOrigin", "https://localhost:5101.evil.invalid")]
    [InlineData("browserOrigin", "https://user@localhost:5101")]
    [InlineData("browserOrigin", "https://localhost:5101?x=1")]
    [InlineData("browserOrigin", "*")]
    [InlineData("issuer", "http://localhost:5101")]
    [InlineData("issuer", "https://localhost:5101#fragment")]
    [InlineData("audience", "")]
    public void Parse_InvalidOriginIssuerOrAudience_FailsSafely(string field, string invalid)
    {
        var policy = Policy();
        policy[field] = invalid;
        AssertRejected(policy.ToJsonString());
    }

    [Theory]
    [InlineData("accessLifetimeSeconds", 901)]
    [InlineData("idleTimeoutSeconds", 299)]
    [InlineData("absoluteLifetimeSeconds", 7776001)]
    [InlineData("clockSkewSeconds", 31)]
    [InlineData("lockoutAttempts", 0)]
    [InlineData("lockoutSeconds", 29)]
    [InlineData("loginWindowSeconds", 3601)]
    [InlineData("loginIdentifierPermits", 21)]
    [InlineData("sourceWindowSeconds", 9)]
    [InlineData("sourcePermits", 0)]
    [InlineData("refreshSourcePermits", 1001)]
    public void Parse_OutOfBoundsNumericPolicy_FailsSafely(string field, int invalid)
    {
        var policy = Policy();
        policy[field] = invalid;
        AssertRejected(policy.ToJsonString());
    }

    [Fact]
    public void Parse_RejectsMissingUnknownDuplicatedAndInconsistentFields()
    {
        var policy = Policy();
        policy.Remove("sourcePermits");
        AssertRejected(policy.ToJsonString());
        policy = Policy();
        policy["unreviewed"] = 1;
        AssertRejected(policy.ToJsonString());
        AssertRejected(Policy().ToJsonString().Replace("\"clockSkewSeconds\":5", "\"clockSkewSeconds\":5,\"ClockSkewSeconds\":5", StringComparison.Ordinal));
        policy = Policy();
        policy["idleTimeoutSeconds"] = 300;
        AssertRejected(policy.ToJsonString());
    }

    private static JsonObject Policy() => JsonSerializer.SerializeToNode(new AuthenticationPolicy(), new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    })!.AsObject();

    private static void AssertRejected(string json)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => AuthenticationPolicy.Parse(json));
        Assert.Equal("Configuration.InvalidAuthenticationPolicy", exception.Message);
        Assert.Null(exception.InnerException);
    }
}
