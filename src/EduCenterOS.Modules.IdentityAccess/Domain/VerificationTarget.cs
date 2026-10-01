using EduCenterOS.BuildingBlocks.Results;
namespace EduCenterOS.Modules.IdentityAccess.Domain;

internal sealed class VerificationTarget
{
    private VerificationTarget() { }
    internal VerificationTarget(byte[] digest)
    {
        if (digest.Length != 32) throw new ArgumentException("IdentityAccess.InvalidTargetDigest");
        Digest = digest.ToArray();
    }
    public byte[] Digest { get; private set; } = [];
    public DateTimeOffset[] IssuesUtc { get; private set; } = [];
    public DateTimeOffset[] VerificationsUtc { get; private set; } = [];
    public DateTimeOffset? LastIssuedAtUtc { get; private set; }
    internal Result ReserveIssue(DateTimeOffset now, int window, int permits, int cooldown)
    {
        RegistrationErrors.RequireUtc(now);
        if (window is < 900 or > 3600 || permits is < 1 or > 3 || cooldown is < 60 or > 300) throw new ArgumentException("IdentityAccess.InvalidIssuePolicy");
        IssuesUtc = IssuesUtc.Where(time => time > now.AddSeconds(-window)).ToArray();
        if (LastIssuedAtUtc is { } last && now < last.AddSeconds(cooldown)) return Result.Failure(RegistrationErrors.RateLimited(Wait(last.AddSeconds(cooldown), now)));
        if (IssuesUtc.Length >= permits) return Result.Failure(RegistrationErrors.RateLimited(Wait(IssuesUtc[0].AddSeconds(window), now)));
        IssuesUtc = [.. IssuesUtc, now]; LastIssuedAtUtc = now; return Result.Success();
    }
    internal Result ReserveVerification(DateTimeOffset now, int window, int permits)
    {
        RegistrationErrors.RequireUtc(now);
        if (window is < 900 or > 3600 || permits is < 1 or > 10) throw new ArgumentException("IdentityAccess.InvalidVerifyPolicy");
        VerificationsUtc = VerificationsUtc.Where(time => time > now.AddSeconds(-window)).ToArray();
        if (VerificationsUtc.Length >= permits) return Result.Failure(RegistrationErrors.RateLimited(Wait(VerificationsUtc[0].AddSeconds(window), now)));
        VerificationsUtc = [.. VerificationsUtc, now]; return Result.Success();
    }
    private static int Wait(DateTimeOffset until, DateTimeOffset now) => Math.Max(1, (int)Math.Ceiling((until - now).TotalSeconds));
}
