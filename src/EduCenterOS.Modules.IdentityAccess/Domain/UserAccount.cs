namespace EduCenterOS.Modules.IdentityAccess.Domain;

internal enum AccountStatus { Active, Suspended, Closed }

internal sealed class UserAccount
{
    private UserAccount() { }
    internal UserAccount(Guid id, Guid personId, string phone, string? email, string passwordHash,
        DateTimeOffset phoneVerifiedAt, DateTimeOffset now)
    {
        RegistrationErrors.RequireUtc(now); RegistrationErrors.RequireUtc(phoneVerifiedAt);
        var normalized = RegistrationInputs.Phone(phone);
        if (id == Guid.Empty || personId == Guid.Empty || !normalized.IsSuccess || normalized.Value != phone
            || (email is not null && (!RegistrationInputs.Email(email).IsSuccess || RegistrationInputs.Email(email).Value != email))
            || string.IsNullOrWhiteSpace(passwordHash) || passwordHash.Length > 1024 || phoneVerifiedAt > now)
            throw new ArgumentException("IdentityAccess.InvalidAccount");
        Id = id; PersonIdentityId = personId; PhoneNumber = NormalizedPhoneNumber = phone;
        EmailAddress = NormalizedEmailAddress = email; PasswordHash = passwordHash;
        PhoneVerifiedAtUtc = phoneVerifiedAt; PasswordChangedAtUtc = CreatedAtUtc = now;
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
}
