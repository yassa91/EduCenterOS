using EduCenterOS.Modules.IdentityAccess.Domain;
using Xunit;

namespace EduCenterOS.UnitTests.IdentityAccess;

public sealed class SessionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SessionExpiresAtExactIdleBoundaryAndNeverRevives()
    {
        var session = new UserSession(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Now, 60, 120);
        Assert.True(session.IsActive(Now.AddSeconds(59)));
        Assert.False(session.IsActive(Now.AddSeconds(60)));
        session.Revoke(Now.AddSeconds(10), "UserRequest");
        Assert.False(session.IsActive(Now.AddSeconds(11)));
        Assert.Throws<InvalidOperationException>(() => session.RefreshActivity(Now.AddSeconds(11), 60));
        var version = session.Version;
        session.Revoke(Now.AddSeconds(12), "LogoutAll");
        Assert.Equal(version, session.Version);
        Assert.Equal(Now.AddSeconds(10), session.RevokedAtUtc);
    }

    [Fact]
    public void RefreshActivityClipsIdleAtAbsoluteWithoutExtendingAbsolute()
    {
        var session = new UserSession(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Now, 60, 90);
        session.RefreshActivity(Now.AddSeconds(59), 60);
        Assert.Equal(Now.AddSeconds(90), session.IdleExpiresAtUtc);
        Assert.Equal(Now.AddSeconds(90), session.AbsoluteExpiresAtUtc);
        Assert.Equal(Now.AddSeconds(59), session.LastSeenAtUtc);
        Assert.False(session.IsActive(Now.AddSeconds(90)));
        Assert.Throws<ArgumentException>(() => session.IsActive(Now.ToOffset(TimeSpan.FromHours(2))));
    }

    [Fact]
    public void RefreshCredentialIsOneUseAndItsExpiryIsInclusive()
    {
        var record = new RefreshTokenRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), new byte[32], Now, Now.AddSeconds(60));
        Assert.True(record.CanConsume(Now.AddSeconds(59)));
        Assert.False(record.CanConsume(Now.AddSeconds(60)));
        var replacement = Guid.CreateVersion7();
        record.Consume(Now.AddSeconds(1), replacement);
        Assert.Equal(replacement, record.ReplacedByTokenId);
        Assert.False(record.CanConsume(Now.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => record.Consume(Now.AddSeconds(2), Guid.CreateVersion7()));
    }

    [Fact]
    public void AccountLockoutUsesInclusiveExpiryAndRehashPreservesSecurityEpoch()
    {
        var account = new UserAccount(Guid.CreateVersion7(), Guid.CreateVersion7(), "+201012345678", null, "synthetic-hash", Now, Now);

        for (var attempt = 0; attempt < 5; attempt++) account.FailAuthentication(Now, 5, 300);

        Assert.Equal(5, account.AccessFailedCount);
        Assert.False(account.CanAuthenticate(Now.AddSeconds(299)));
        Assert.True(account.CanAuthenticate(Now.AddSeconds(300)));
        account.FailAuthentication(Now.AddSeconds(300), 5, 300);
        Assert.Equal(1, account.AccessFailedCount);
        account.CompleteAuthentication(Now.AddSeconds(301), "synthetic-rehash");
        Assert.Equal(0, account.AccessFailedCount);
        Assert.Null(account.LockoutEndUtc);
        Assert.Equal(1, account.SecurityVersion);
        Assert.Equal(Now, account.PasswordChangedAtUtc);
    }

    [Fact]
    public void LoginBudgetsCoverWholeWindowWithoutResetOnNewAttempts()
    {
        var target = new LoginTarget(new byte[32]);
        Assert.Null(target.Reserve(Now, 900, 2));
        Assert.Null(target.Reserve(Now.AddSeconds(1), 900, 2));
        Assert.Equal(898, target.Reserve(Now.AddSeconds(2), 900, 2));
        Assert.Null(target.Reserve(Now.AddSeconds(900), 900, 2));
        Assert.Equal(2, target.AttemptsUtc.Length);
    }
}
