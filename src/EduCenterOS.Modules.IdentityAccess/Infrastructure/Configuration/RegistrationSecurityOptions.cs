using System.Text.Json;
using System.Text.Json.Serialization;
namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Configuration;

internal sealed class RegistrationSecurityOptions
{
    public int CodeLifetimeSeconds { get; init; } = 300;
    public int ProofLifetimeSeconds { get; init; } = 300;
    public int MaximumFailedAttempts { get; init; } = 5;
    public int MinimumResendSeconds { get; init; } = 60;
    public int RollingWindowSeconds { get; init; } = 900;
    public int TargetIssuePermits { get; init; } = 3;
    public int TargetVerifyPermits { get; init; } = 10;
    public int PasswordMinimumLength { get; init; } = 12;
    public int PasswordMaximumLength { get; init; } = 128;
    public int PasswordIterations { get; init; } = 210000;
    public int BucketCount { get; init; } = 4096;
    public int AnonymousIpPermits { get; init; } = 120;
    public int IssueIpPermits { get; init; } = 20;
    public int VerifyIpPermits { get; init; } = 30;
    internal static RegistrationSecurityOptions Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var expected = typeof(RegistrationSecurityOptions).GetProperties().Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).ToHashSet(StringComparer.Ordinal);
        var fields = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        if (fields.Length != expected.Count || !fields.ToHashSet(StringComparer.Ordinal).SetEquals(expected)) throw new InvalidOperationException();
        var options = JsonSerializer.Deserialize<RegistrationSecurityOptions>(json, new JsonSerializerOptions
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidOperationException();
        if (options.CodeLifetimeSeconds is < 60 or > 600 || options.ProofLifetimeSeconds is < 60 or > 600
            || options.MaximumFailedAttempts is < 1 or > 10 || options.MinimumResendSeconds is < 60 or > 300
            || options.MinimumResendSeconds > options.CodeLifetimeSeconds || options.RollingWindowSeconds is < 900 or > 3600
            || options.TargetIssuePermits is < 1 or > 3 || options.TargetVerifyPermits is < 1 or > 10
            || options.PasswordMinimumLength is < 12 or > 32 || options.PasswordMaximumLength is < 64 or > 128
            || options.PasswordMinimumLength > options.PasswordMaximumLength || options.PasswordIterations is < 210000 or > 1000000
            || options.BucketCount is < 64 or > 4096 || options.AnonymousIpPermits is < 1 or > 120
            || options.IssueIpPermits is < 1 or > 20 || options.VerifyIpPermits is < 1 or > 30)
            throw new InvalidOperationException();
        return options;
    }
}
