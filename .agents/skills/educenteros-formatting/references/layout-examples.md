# Approved C# layouts

Use these examples with this skill's [readability rules](../SKILL.md). All values below are
illustrative; formatting existing code preserves its original values and tokens.

## Error declarations and call sites

```csharp
internal static Error VerificationRejected => new(
    "IdentityAccess.VerificationRejected",
    ErrorCategory.BusinessRule,
    "Verification could not be completed."
);
```

For an explicit construction nested in a call, the closing `)` for `Error` aligns
with the enclosing statement:

```csharp
return Result.Failure(new Error(
    "IdentityAccess.VerificationRejected",
    ErrorCategory.BusinessRule,
    "Verification could not be completed."
));
```

Retain the argument order and apply the same layout to additional arguments.

## Multiline conditions

```csharp
if (
    id == Guid.Empty || !phone.IsSuccess || phone.Value != target ||
    hash.Length != 32 || string.IsNullOrWhiteSpace(keyVersion) ||
    keyVersion.Length > 32 || lifetimeSeconds is < 60 or > 600
)
{
    throw new ArgumentException("IdentityAccess.InvalidChallenge");
}
```

Wrapping must not reorder short-circuit terms or add/remove parentheses. Do not
split every term onto its own line when the existing readable grouping suffices.

## Validation, state changes, and return

```csharp
internal Result FailAttempt(DateTimeOffset now, int maximum)
{
    if (maximum is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(maximum));

    if (!CanVerify(now)) return Result.Failure(RegistrationErrors.VerificationRejected);

    FailedAttempts++;

    if (FailedAttempts >= maximum) Status = ChallengeStatus.Locked;

    return Result.Failure(RegistrationErrors.VerificationRejected);
}
```

Choose blank lines around coherent steps. Do not add one between every statement,
and do not extract a helper method merely to shorten the layout.

## Properties and attributes

```csharp
public Guid Id { get; private set; }
public DateTimeOffset CreatedAtUtc { get; private set; }
public DateTimeOffset ExpiresAtUtc { get; private set; }

[Fact]
[Trait("Area", "IdentityAccess")]
public void Verification_ExpiredChallenge_IsRejected()
{
    // Keep the existing test body and assertions.
}
```

Each attribute list occupies its own line; do not split the attributes within a
single existing list by changing its syntax. The sample test body only illustrates
placement and is not a test to copy into the project.
