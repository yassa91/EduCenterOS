using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Configuration;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Xunit;
namespace EduCenterOS.UnitTests.IdentityAccess;

public sealed class RegistrationSecurityTests
{
    private static string Policy => System.Text.Json.JsonSerializer.Serialize(new RegistrationSecurityOptions(), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
    private static IdentityAccessRuntimeSettings Settings(string? policy = null, Dictionary<string,string>? keys = null, string version = "v1", string? partition = null)
        => IdentityAccessRuntimeSettings.FromSnapshot("Host=127.0.0.1;Database=security_tests;Username=educenteros_identity_runtime;Password=synthetic",
            keys ?? new() { ["v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }, version,
            partition ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), policy ?? Policy, "Testing", null);
    [Theory]
    [InlineData("codeLifetimeSeconds")] [InlineData("proofLifetimeSeconds")] [InlineData("maximumFailedAttempts")]
    [InlineData("minimumResendSeconds")] [InlineData("rollingWindowSeconds")] [InlineData("targetIssuePermits")]
    [InlineData("targetVerifyPermits")] [InlineData("passwordMinimumLength")] [InlineData("passwordMaximumLength")]
    [InlineData("passwordIterations")] [InlineData("bucketCount")] [InlineData("anonymousIpPermits")]
    [InlineData("issueIpPermits")] [InlineData("verifyIpPermits")]
    public void EverySecurityOption_RejectsOutOfBoundsAndMissingValues(string field)
    {
        var policy = JsonNode.Parse(Policy)!.AsObject(); policy[field] = 0;
        Assert.Throws<InvalidOperationException>(() => Settings(policy.ToJsonString()));
        policy.Remove(field); Assert.Throws<InvalidOperationException>(() => Settings(policy.ToJsonString()));
    }
    [Fact]
    public void PolicyAndKeyInventory_RejectAmbiguityAndPurposeReuse()
    {
        var policy = JsonNode.Parse(Policy)!.AsObject(); policy["unexpected"] = 1;
        Assert.Throws<InvalidOperationException>(() => Settings(policy.ToJsonString()));
        policy.Remove("unexpected"); policy["codeLifetimeSeconds"] = 60; policy["minimumResendSeconds"] = 120;
        Assert.Throws<InvalidOperationException>(() => Settings(policy.ToJsonString()));
        Assert.Throws<InvalidOperationException>(() => Settings(keys: new() { ["v1"] = "diagnostic-sensitive-marker" }));
        Assert.Throws<InvalidOperationException>(() => Settings(version: "unknown"));
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Assert.Throws<InvalidOperationException>(() => Settings(keys: new() { ["v1"] = key }, partition: key));
        Assert.Throws<InvalidOperationException>(() => Settings(keys: new() { ["v1"] = key, ["V1"] = key }));
    }
    [Fact]
    public void CodeAndProofHashes_AreBoundToChallengeAndTargetAndVersion()
    {
        var crypto = new RegistrationCryptography(Settings()); var id = Guid.CreateVersion7(); var code = crypto.GenerateCode();
        Assert.Equal(6, code.Length); Assert.True(code.All(char.IsAsciiDigit));
        var hash = crypto.HashCode(id, "+201012345678", code, "v1");
        var challenge = new OtpChallenge(id, "+201012345678", hash, "v1", DateTimeOffset.UtcNow, 300);
        Assert.True(crypto.MatchesCode(challenge, code));
        Assert.False(CryptographicOperations.FixedTimeEquals(hash, crypto.HashCode(Guid.CreateVersion7(), "+201012345678", code, "v1")));
        Assert.False(CryptographicOperations.FixedTimeEquals(hash, crypto.HashCode(id, "+201112345678", code, "v1")));
        Assert.Throws<InvalidOperationException>(() => crypto.HashCode(id, "+201012345678", code, "retired"));
        var proof = crypto.GenerateProof(); Assert.True(RegistrationCryptography.IsProof(proof)); Assert.Equal(43, proof.Length);
        Assert.False(RegistrationCryptography.IsProof(proof + "=")); Assert.False(RegistrationCryptography.IsProof(new string('!',43)));
        Assert.False(CryptographicOperations.FixedTimeEquals(crypto.HashProof(id, "+201012345678", proof), crypto.HashProof(id, "+201112345678", proof)));
    }
    [Fact]
    public void RetainedOtpKeys_VerifyOldChallengeWithoutCurrentKeyFallback()
    {
        var keys = new Dictionary<string,string> { ["v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), ["v2"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
        var crypto = new RegistrationCryptography(Settings(keys: keys, version: "v2")); var id = Guid.CreateVersion7(); var code = crypto.GenerateCode();
        var old = new OtpChallenge(id, "+201012345678", crypto.HashCode(id, "+201012345678", code, "v1"), "v1", DateTimeOffset.UtcNow, 300);
        Assert.True(crypto.MatchesCode(old, code));
        var retired = new RegistrationCryptography(Settings(keys: new() { ["v2"] = keys["v2"] }, version: "v2"));
        Assert.False(retired.MatchesCode(old, code));
    }
    [Fact]
    public void ApiPartitions_AreStableAndBoundedForArbitrarySignals()
    {
        var crypto = new RegistrationCryptography(Settings());
        for (var i=0; i<10000; i++) Assert.InRange(crypto.Bucket("signal-"+i), 0, 4095);
        Assert.Equal(crypto.Bucket("missing"), crypto.Bucket("missing"));
    }
}
