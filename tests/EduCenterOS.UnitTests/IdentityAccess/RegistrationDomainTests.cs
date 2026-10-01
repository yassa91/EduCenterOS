using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Domain;
using Xunit;

namespace EduCenterOS.UnitTests.IdentityAccess;

public sealed class RegistrationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static OtpChallenge Challenge() => new(Guid.CreateVersion7(), "+201012345678", new byte[32], "v1", Now, 300);

    [Theory]
    [InlineData("01012345678")]
    [InlineData("+201012345678")]
    [InlineData("00201012345678")]
    [InlineData(" 01012345678 ")]
    public void EquivalentPhoneInputs_HaveOneCanonicalValue(string input) => Assert.Equal("+201012345678", RegistrationInputs.Phone(input).Value);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("٠١٠١٢٣٤٥٦٧٨")]
    [InlineData("+201312345678")] [InlineData("010 12345678")] [InlineData("+14155551212")]
    public void InvalidPhones_AreSafeValidationFailures(string? input)
    {
        var result = RegistrationInputs.Phone(input); Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.Validation, result.Error.Category);
    }

    [Fact]
    public void NamesAndEmails_NormalizeWithoutLegalIdentityRules()
    {
        Assert.Equal("يوسف مجدي", RegistrationInputs.Name("  يوسف مجدي ").Value);
        Assert.Equal("user@example.com", RegistrationInputs.Email(" User@Example.COM ").Value);
        Assert.False(RegistrationInputs.Email("User <user@example.com>").IsSuccess);
        Assert.False(RegistrationInputs.Email("").IsSuccess);
        Assert.False(RegistrationInputs.Email("a..b@example.com").IsSuccess);
        Assert.False(RegistrationInputs.Name("x\u0000y").IsSuccess);
        Assert.False(RegistrationInputs.Password("short", 12, 128));
        Assert.True(RegistrationInputs.Password(" twelve spaces ", 12, 128));
    }

    [Theory]
    [InlineData(299, true)] [InlineData(300, false)] [InlineData(301, false)]
    public void CodeExpiry_IsExactAndUsesSuppliedUtc(int seconds, bool expected)
    {
        var clock = new TestClock(Now.AddSeconds(seconds));
        Assert.Equal(expected, Challenge().CanVerify(clock.UtcNow));
    }

    [Fact]
    public void AttemptsLock_AndVerificationCannotBeRepeated()
    {
        var challenge = Challenge();
        for (var i = 0; i < 5; i++) Assert.False(challenge.FailAttempt(Now, 5).IsSuccess);
        Assert.Equal(ChallengeStatus.Locked, challenge.Status); Assert.Equal(5, challenge.FailedAttempts);
        Assert.False(challenge.Verify(Now, new byte[32], 300).IsSuccess);
        var verified = Challenge(); Assert.True(verified.Verify(Now, new byte[32], 300).IsSuccess);
        Assert.False(verified.Verify(Now, new byte[32], 300).IsSuccess);
    }

    [Theory]
    [InlineData(299, true)] [InlineData(300, false)] [InlineData(301, false)]
    public void ProofExpiry_IsExactAndIndependentFromCode(int seconds, bool expected)
    {
        var challenge = Challenge(); Assert.True(challenge.Verify(Now.AddSeconds(299), new byte[32], 300).IsSuccess);
        Assert.Equal(expected, challenge.CanConsume(Now.AddSeconds(299 + seconds)));
    }

    [Fact]
    public void ProofConsumptionAndResendInvalidation_AreOneUse()
    {
        var challenge = Challenge(); challenge.Verify(Now, new byte[32], 300);
        Assert.True(challenge.Consume(Now).IsSuccess); Assert.Null(challenge.ProofHash);
        Assert.False(challenge.Consume(Now).IsSuccess);
        var old = Challenge(); old.Verify(Now, new byte[32], 300); old.Invalidate();
        Assert.False(old.CanConsume(Now)); Assert.Null(old.ProofHash);
    }

    [Fact]
    public void TargetBudgets_KeepCooldownAndRollingHistoryAcrossChallenges()
    {
        var target = new VerificationTarget(new byte[32]);
        Assert.True(target.ReserveIssue(Now, 900, 3, 60).IsSuccess);
        Assert.Equal(1, target.ReserveIssue(Now.AddSeconds(59), 900, 3, 60).Error.RetryDelaySeconds);
        Assert.True(target.ReserveIssue(Now.AddSeconds(60), 900, 3, 60).IsSuccess);
        Assert.True(target.ReserveIssue(Now.AddSeconds(120), 900, 3, 60).IsSuccess);
        Assert.False(target.ReserveIssue(Now.AddSeconds(180), 900, 3, 60).IsSuccess);
        Assert.True(target.ReserveIssue(Now.AddSeconds(900), 900, 3, 60).IsSuccess);
        for (var i = 0; i < 10; i++) Assert.True(target.ReserveVerification(Now, 900, 10).IsSuccess);
        Assert.False(target.ReserveVerification(Now, 900, 10).IsSuccess);
        Assert.True(target.ReserveVerification(Now.AddSeconds(900), 900, 10).IsSuccess);
    }

    [Fact]
    public void NewAccount_HasDistinctExplicitIdentityAndInitialSecurityState()
    {
        var personId = Guid.CreateVersion7(); var accountId = Guid.CreateVersion7();
        var person = new PersonIdentity(personId, "Example User", Now);
        var account = new UserAccount(accountId, person.Id, "+201012345678", "user@example.com", "test-hash", Now, Now);
        Assert.NotEqual(account.Id, person.Id); Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal(1, account.SecurityVersion); Assert.Equal(0, account.AccessFailedCount);
        Assert.Null(account.InitialOnboardingIntent); Assert.Null(account.EmailVerifiedAtUtc);
        Assert.Equal(Now, account.PhoneVerifiedAtUtc);
        Assert.Throws<ArgumentException>(() => new PersonIdentity(Guid.Empty, "Example User", Now));
        Assert.Throws<ArgumentException>(() => new UserAccount(accountId, personId, "01012345678", null, "test-hash", Now, Now));
        Assert.Throws<ArgumentException>(() => Challenge().CanVerify(Now.ToOffset(TimeSpan.FromHours(2))));
    }
    [Theory]
    [InlineData("user@example.com\n")] [InlineData("\tuser@example.com")] [InlineData("user@example.com\0")]
    public void EmailControlCharacters_AreRejectedBeforeTrim(string input)=>Assert.False(RegistrationInputs.Email(input).IsSuccess);
}

internal sealed class TestClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

}
