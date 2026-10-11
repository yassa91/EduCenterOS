namespace EduCenterOS.Modules.IdentityAccess.Domain;

internal enum AccountStatus
{
    Active,
    Suspended,
    Closed
}

internal sealed class UserAccount
{
    private UserAccount()
    {
    }

    internal UserAccount(
        Guid id,
        Guid personId,
        string phone,
        string? email,
        string passwordHash,
        DateTimeOffset phoneVerifiedAt,
        DateTimeOffset now
    )
    {
        RegistrationErrors.RequireUtc(now);
        RegistrationErrors.RequireUtc(phoneVerifiedAt);
        var normalized = RegistrationInputs.Phone(phone);

        if (
            id == Guid.Empty ||
            personId == Guid.Empty ||
            !normalized.IsSuccess ||
            normalized.Value != phone ||
            !IsNormalizedEmail(email) ||
            string.IsNullOrWhiteSpace(passwordHash) ||
            passwordHash.Length > 1024 ||
            phoneVerifiedAt > now
        )
            throw new ArgumentException("IdentityAccess.InvalidAccount");

        Id = id;
        PersonIdentityId = personId;
        PhoneNumber = NormalizedPhoneNumber = phone;
        EmailAddress = NormalizedEmailAddress = email;
        PasswordHash = passwordHash;
        PhoneVerifiedAtUtc = phoneVerifiedAt;
        PasswordChangedAtUtc = CreatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid PersonIdentityId { get; private set; }
    public string PhoneNumber { get; private set; } = string.Empty;
    public string NormalizedPhoneNumber { get; private set; } = string.Empty;
    public DateTimeOffset PhoneVerifiedAtUtc { get; private set; }
    public string? EmailAddress { get; private set; }
    public string? NormalizedEmailAddress { get; private set; }
    public DateTimeOffset? EmailVerifiedAtUtc { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTimeOffset PasswordChangedAtUtc { get; private set; }
    public AccountStatus Status { get; private set; } = AccountStatus.Active;
    public string? InitialOnboardingIntent { get; private set; }
    public long SecurityVersion { get; private set; } = 1;
    public int AccessFailedCount { get; private set; }
    public DateTimeOffset? LockoutEndUtc { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal bool CanAuthenticate(DateTimeOffset now)
    {
        RegistrationErrors.RequireUtc(now);

        return Status == AccountStatus.Active && now >= CreatedAtUtc && PhoneVerifiedAtUtc <= now &&
            (LockoutEndUtc is null || now >= LockoutEndUtc);
    }

    internal void FailAuthentication(DateTimeOffset now, int threshold, int lockoutSeconds)
    {
        RegistrationErrors.RequireUtc(now);

        if (threshold is < 1 or > 20 || lockoutSeconds is < 30 or > 3600)
            throw new ArgumentException("IdentityAccess.InvalidLockoutPolicy");

        if (!CanAuthenticate(now)) return;

        if (LockoutEndUtc is not null)
        {
            AccessFailedCount = 0;
            LockoutEndUtc = null;
        }

        AccessFailedCount++;

        if (AccessFailedCount >= threshold) LockoutEndUtc = now.AddSeconds(lockoutSeconds);

        Version++;
    }

    internal void CompleteAuthentication(DateTimeOffset now, string passwordHash)
    {
        if (!CanAuthenticate(now) || string.IsNullOrWhiteSpace(passwordHash) || passwordHash.Length > 1024)
            throw new InvalidOperationException("IdentityAccess.AuthenticationRejected");

        AccessFailedCount = 0;
        LockoutEndUtc = null;
        PasswordHash = passwordHash;
        Version++;
    }

    private static bool IsNormalizedEmail(string? email)
    {
        if (email is null) return true;

        var normalized = RegistrationInputs.Email(email);

        return normalized.IsSuccess && normalized.Value == email;
    }
}
