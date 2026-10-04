namespace EduCenterOS.Modules.IdentityAccess.Domain;

internal sealed class LoginTarget
{
    private LoginTarget()
    {
    }

    internal LoginTarget(byte[] digest)
    {
        if (digest.Length != 32) throw new ArgumentException("IdentityAccess.InvalidLoginTarget");

        Digest = digest.ToArray();
    }

    public byte[] Digest { get; private set; } = [];
    public DateTimeOffset[] AttemptsUtc { get; private set; } = [];
    public DateTimeOffset LastAttemptAtUtc { get; private set; }

    internal int? Reserve(DateTimeOffset now, int windowSeconds, int permits)
    {
        RegistrationErrors.RequireUtc(now);

        if (windowSeconds is < 60 or > 3600 || permits is < 1 or > 20)
            throw new ArgumentException("IdentityAccess.InvalidLoginBudget");

        AttemptsUtc = AttemptsUtc.Where(value => value > now.AddSeconds(-windowSeconds)).Order().ToArray();

        if (AttemptsUtc.Length >= permits)
            return Math.Max(1, (int)Math.Ceiling((AttemptsUtc[0].AddSeconds(windowSeconds) - now).TotalSeconds));

        AttemptsUtc = [.. AttemptsUtc, now];
        LastAttemptAtUtc = now;

        return null;
    }
}
