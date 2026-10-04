using System.Security.Cryptography;
using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Features.RegisterAccount;

internal sealed class RegisterAccountHandler(
    RegistrationTransactions transactions,
    IDbContextFactory<IdentityAccessDbContext> factory,
    IdentityAccessRuntimeSettings settings,
    RegistrationCryptography crypto,
    IPasswordHasher<UserAccount> hasher
)
{
    internal async Task<Result<RegisterAccountResponse>> HandleAsync(RegisterAccountRequest request, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        var name = RegistrationInputs.Name(request.FullName);
        var email = request.EmailAddress is null ? null : RegistrationInputs.Email(request.EmailAddress);

        if (!name.IsSuccess) issues.AddRange(name.Error.ValidationIssues);

        if (email is { IsSuccess: false }) issues.AddRange(email.Error.ValidationIssues);

        if (request.ChallengeId == Guid.Empty) issues.AddRange(RegistrationErrors.Field("challengeId").ValidationIssues);

        if (!RegistrationCryptography.IsProof(request.VerificationProof)) issues.AddRange(RegistrationErrors.Field("verificationProof").ValidationIssues);

        if (
            !RegistrationInputs.Password(request.Password, settings.Policy.PasswordMinimumLength, settings.Policy.PasswordMaximumLength)
        )
            issues.AddRange(RegistrationErrors.Field("password").ValidationIssues);

        if (issues.Count > 0) return Result<RegisterAccountResponse>.Failure(Error.Validation(issues));

        var normalizedEmail = email?.Value;
        await using var lookup = await factory.CreateDbContextAsync(cancellationToken);
        var source = await lookup.Challenges.AsNoTracking().SingleOrDefaultAsync(value => value.Id == request.ChallengeId, cancellationToken);

        if (source is null) return Result<RegisterAccountResponse>.Failure(RegistrationErrors.VerificationRejected);

        return await transactions.RunAsync(RegistrationOperation.Register, source.NormalizedTarget, async (context, now, token) =>
        {
            var challenge = await context.Challenges.SingleOrDefaultAsync(value => value.Id == request.ChallengeId, token);

            if (
                challenge is null ||
                !challenge.CanConsume(now) ||
                !CryptographicOperations.FixedTimeEquals(challenge.ProofHash!, crypto.HashProof(challenge.Id, challenge.NormalizedTarget, request.VerificationProof!))
            )
                return Result<RegisterAccountResponse>.Failure(RegistrationErrors.VerificationRejected);

            if (
                await context.Accounts.AnyAsync(value => value.NormalizedPhoneNumber == challenge.NormalizedTarget
                    || (normalizedEmail != null && value.NormalizedEmailAddress == normalizedEmail), token)
            )
                return Result<RegisterAccountResponse>.Failure(RegistrationErrors.RegistrationRejected);

            // PasswordHasher's Identity V3 primitive does not use its user parameter. Raw password never enters the domain model.
            var hash = hasher.HashPassword(null!, request.Password!);
            var person = new PersonIdentity(Guid.CreateVersion7(now), name.Value, now);
            var account = new UserAccount(Guid.CreateVersion7(now), person.Id, challenge.NormalizedTarget, normalizedEmail, hash, challenge.VerifiedAtUtc!.Value, now);
            context.People.Add(person);
            context.Accounts.Add(account);
            var consumed = challenge.Consume(now);

            if (!consumed.IsSuccess) throw new InvalidOperationException("IdentityAccess.AuthoritativeProofChanged");

            return Result<RegisterAccountResponse>.Success(new(account.Id, person.Id, now));
        }, cancellationToken);
    }
}
