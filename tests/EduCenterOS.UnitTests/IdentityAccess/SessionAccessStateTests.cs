using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Xunit;

namespace EduCenterOS.UnitTests.IdentityAccess;

public sealed class SessionAccessStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(59, true)]
    [InlineData(60, false)]
    [InlineData(90, false)]
    public void AuthorizationSnapshot_RespectsCreationAndExactExpiration(int elapsedSeconds, bool expected)
    {
        var (account, session) = CreateSession();
        var state = SessionAccessState.From(account, session);
        var actor = new AuthenticatedActor(account.Id, session.Id, account.SecurityVersion);

        Assert.Equal(expected, state.AcceptsActor(actor, Now.AddSeconds(elapsedSeconds)));
    }

    [Fact]
    public void AuthorizationSnapshot_RejectsWrongAccountSessionAndSecurityVersion()
    {
        var (account, session) = CreateSession();
        var state = SessionAccessState.From(account, session);
        var actor = new AuthenticatedActor(account.Id, session.Id, account.SecurityVersion);

        Assert.True(state.AcceptsActor(actor, Now));
        Assert.False(state.AcceptsActor(actor with { AccountId = Guid.NewGuid() }, Now));
        Assert.False(state.AcceptsActor(actor with { SessionId = Guid.NewGuid() }, Now));
        Assert.False(state.AcceptsActor(actor with { SecurityVersion = 2 }, Now));
    }

    [Fact]
    public void AuthorizationSnapshot_RejectsRevokedOrStaleSession()
    {
        var (account, session) = CreateSession();
        session.Revoke(Now, "UserRequest");

        Assert.False(SessionAccessState.From(account, session).OwnsLiveSession(Now));

        var stale = new UserSession(Guid.NewGuid(), account.Id, 2, Now, 60, 90);
        Assert.False(SessionAccessState.From(account, stale).OwnsLiveSession(Now));
    }

    [Fact]
    public void AuthorizationSnapshot_RejectsForeignSessionAndNonUtcClock()
    {
        var (account, session) = CreateSession();
        var foreign = new UserSession(Guid.NewGuid(), Guid.NewGuid(), 1, Now, 60, 90);

        Assert.False(SessionAccessState.From(account, foreign).OwnsLiveSession(Now));
        Assert.Throws<ArgumentException>(() => SessionAccessState.From(account, session).OwnsLiveSession(Now.ToOffset(TimeSpan.FromHours(2))));
    }

    private static (UserAccount Account, UserSession Session) CreateSession()
    {
        var account = new UserAccount(Guid.NewGuid(), Guid.NewGuid(), "+201012345678", null, "synthetic-hash", Now, Now);
        var session = new UserSession(Guid.NewGuid(), account.Id, account.SecurityVersion, Now, 60, 90);

        return (account, session);
    }
}
