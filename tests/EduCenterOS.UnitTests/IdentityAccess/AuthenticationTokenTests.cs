using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Configuration;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace EduCenterOS.UnitTests.IdentityAccess;

public sealed class AuthenticationTokenTests
{
    private static readonly RSA OldKey = RSA.Create(2048);
    private static readonly RSA NewKey = RSA.Create(2048);
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issue_UsesStrictSignedProfileAndBoundedSessionExpiry()
    {
        using var tokens = Service(OldKey, "old", new() { ["old"] = OldKey.ExportSubjectPublicKeyInfoPem() });
        var session = new UserSession(Guid.NewGuid(), Guid.NewGuid(), 7, Now, 300, 900);
        var issued = tokens.Issue(session, Now);
        new JwtSecurityTokenHandler().ValidateToken(issued.Value, tokens.ValidationParameters(), out _);
        Assert.True(tokens.TryReadActor(issued.Value, out var actor));
        Assert.Equal(session.UserAccountId, actor.AccountId);
        Assert.Equal(session.Id, actor.SessionId);
        Assert.Equal(7, actor.SecurityVersion);
        Assert.Equal(Now.AddSeconds(300), issued.ExpiresAtUtc);
        Assert.DoesNotContain(issued.Value, issued.ToString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotation_RetainedPublicKeyValidatesOldTokensAndNewKeySignsNewTokens()
    {
        using var old = Service(OldKey, "old", new() { ["old"] = OldKey.ExportSubjectPublicKeyInfoPem() });
        using var rotated = Service(NewKey, "new", new() { ["old"] = OldKey.ExportSubjectPublicKeyInfoPem(), ["new"] = NewKey.ExportSubjectPublicKeyInfoPem() });
        var session = new UserSession(Guid.NewGuid(), Guid.NewGuid(), 1, Now, 600, 900);
        var issuedOld = old.Issue(session, Now);
        new JwtSecurityTokenHandler().ValidateToken(issuedOld.Value, rotated.ValidationParameters(), out _);
        var issuedNew = rotated.Issue(session, Now);
        new JwtSecurityTokenHandler().ValidateToken(issuedNew.Value, rotated.ValidationParameters(), out _);
        Assert.True(rotated.TryReadActor(issuedOld.Value, out _));
        Assert.True(rotated.TryReadActor(issuedNew.Value, out _));
        Assert.Equal("new", new JwtSecurityTokenHandler().ReadJwtToken(issuedNew.Value).Header.Kid);
    }

    [Theory]
    [InlineData("wrongSignature")]
    [InlineData("unknownKid")]
    [InlineData("wrongType")]
    [InlineData("wrongIssuer")]
    [InlineData("wrongAudience")]
    [InlineData("expired")]
    [InlineData("futureIssued")]
    [InlineData("extraClaim")]
    [InlineData("duplicateClaim")]
    [InlineData("stringVersion")]
    [InlineData("emptySession")]
    [InlineData("remoteKeyHeader")]
    [InlineData("notBefore")]
    [InlineData("oversizedLifetime")]
    public void Validation_RejectsForgedOrMalformedProfile(string fault)
    {
        using var tokens = Service(OldKey, "old", new() { ["old"] = OldKey.ExportSubjectPublicKeyInfoPem() });
        var header = new Dictionary<string, object> { ["alg"] = "RS256", ["kid"] = "old", ["typ"] = "educenteros-access+jwt" };
        var claims = new Dictionary<string, object>
        {
            ["iss"] = "https://localhost:5101", ["aud"] = "educenteros-api", ["sub"] = Guid.NewGuid().ToString("D"),
            ["sid"] = Guid.NewGuid().ToString("D"), ["jti"] = Guid.NewGuid().ToString("D"), ["sv"] = 1L,
            ["iat"] = Now.ToUnixTimeSeconds(), ["exp"] = Now.AddSeconds(600).ToUnixTimeSeconds()
        };
        if (fault == "unknownKid") header["kid"] = "other";
        if (fault == "wrongType") header["typ"] = "JWT";
        if (fault == "wrongIssuer") claims["iss"] = "https://other.invalid";
        if (fault == "wrongAudience") claims["aud"] = "other";
        if (fault == "expired") claims["exp"] = Now.AddMinutes(-2).ToUnixTimeSeconds();
        if (fault == "futureIssued") claims["iat"] = Now.AddMinutes(1).ToUnixTimeSeconds();
        if (fault == "extraClaim") claims["role"] = "Owner";
        if (fault == "stringVersion") claims["sv"] = "1";
        if (fault == "emptySession") claims["sid"] = Guid.Empty.ToString("D");
        if (fault == "remoteKeyHeader") header["jku"] = "https://other.invalid/keys";
        if (fault == "notBefore") claims["nbf"] = Now.ToUnixTimeSeconds();
        if (fault == "oversizedLifetime") claims["exp"] = Now.AddHours(1).ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(claims);
        if (fault == "duplicateClaim") payload = payload[..^1] + ",\"sv\":1}";
        var signingInput = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header))) + "." + Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload));
        var signature = (fault == "wrongSignature" ? NewKey : OldKey).SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var encoded = signingInput + "." + Base64UrlEncoder.Encode(signature);
        var accepted = false;
        try
        {
            new JwtSecurityTokenHandler().ValidateToken(encoded, tokens.ValidationParameters(), out _);
            accepted = tokens.TryReadActor(encoded, out _);
        }
        catch (SecurityTokenException)
        {
        }
        Assert.False(accepted, "The invalid authentication profile must be rejected.");
    }

    [Theory]
    [InlineData("missingCurrent")]
    [InlineData("mismatchedKey")]
    [InlineData("privateInPublicRing")]
    [InlineData("invalidKid")]
    [InlineData("weakKey")]
    [InlineData("badPolicy")]
    public void RuntimeSettings_RejectInvalidKeysOrPolicyWithoutDisclosure(string fault)
    {
        var privateKey = OldKey.ExportPkcs8PrivateKeyPem();
        var ring = new Dictionary<string, string> { ["old"] = OldKey.ExportSubjectPublicKeyInfoPem() };
        var policy = Policy();
        var kid = "old";
        if (fault == "missingCurrent") kid = "absent";
        if (fault == "mismatchedKey") ring["old"] = NewKey.ExportSubjectPublicKeyInfoPem();
        if (fault == "privateInPublicRing") ring["old"] = privateKey;
        if (fault == "invalidKid")
        {
            ring["OLD"] = ring["old"];
            ring.Remove("old");
            kid = "OLD";
        }
        if (fault == "weakKey")
        {
            using var weak = RSA.Create(1024);
            privateKey = weak.ExportPkcs8PrivateKeyPem();
            ring["old"] = weak.ExportSubjectPublicKeyInfoPem();
        }
        if (fault == "badPolicy") policy = policy.Replace("\"clockSkewSeconds\":5", "\"clockSkewSeconds\":60", StringComparison.Ordinal);
        var exception = Assert.Throws<InvalidOperationException>(() => AuthenticationRuntimeSettings.FromSnapshot(policy, privateKey, kid, ring));
        Assert.Equal("Configuration.InvalidAuthenticationSettings", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("BEGIN", exception.ToString(), StringComparison.Ordinal);
    }

    private static AuthenticationTokens Service(RSA key, string kid, Dictionary<string, string> ring) =>
        new(AuthenticationRuntimeSettings.FromSnapshot(Policy(), key.ExportPkcs8PrivateKeyPem(), kid, ring), new FixedClock());

    private static string Policy() => JsonSerializer.Serialize(new AuthenticationPolicy(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
