using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Delivery;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
namespace EduCenterOS.Modules.IdentityAccess.Features.VerifyPhone;

internal sealed class VerifyPhoneRequest { public required string? Code { get; init; } }
internal sealed class VerifyPhoneResponse(string verificationProof, DateTimeOffset expiresAtUtc)
{
    public string VerificationProof { get; } = verificationProof;
    public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
}
internal sealed class VerifyPhoneHandler(RegistrationTransactions transactions, IDbContextFactory<IdentityAccessDbContext> factory,
    IdentityAccessRuntimeSettings settings, RegistrationCryptography crypto, IOtpSender sender)
{
    internal async Task<Result<VerifyPhoneResponse>> HandleAsync(Guid id, string? code, CancellationToken cancellationToken)
    {
        if (code is not { Length: 6 } || !code.All(char.IsAsciiDigit)) return Result<VerifyPhoneResponse>.Failure(RegistrationErrors.Field("code"));
        await using var lookup = await factory.CreateDbContextAsync(cancellationToken);
        var source = await lookup.Challenges.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (source is null) return Result<VerifyPhoneResponse>.Failure(RegistrationErrors.VerificationRejected);
        var result = await transactions.RunAsync(RegistrationOperation.Verify, source.NormalizedTarget, async (context, now, token) =>
        {
            var target = await RegistrationTransactions.TargetAsync(context, crypto.TargetDigest(source.NormalizedTarget), token);
            var quota = target.ReserveVerification(now, settings.Policy.RollingWindowSeconds, settings.Policy.TargetVerifyPermits);
            if (!quota.IsSuccess) return Result<VerifyPhoneResponse>.Failure(quota.Error);
            var challenge = await context.Challenges.SingleOrDefaultAsync(value => value.Id == id, token);
            if (challenge is null || !challenge.CanVerify(now)) return Result<VerifyPhoneResponse>.Failure(RegistrationErrors.VerificationRejected);
            if (!crypto.MatchesCode(challenge, code))
            {
                challenge.FailAttempt(now, settings.Policy.MaximumFailedAttempts);
                return Result<VerifyPhoneResponse>.Failure(RegistrationErrors.VerificationRejected);
            }
            var proof = crypto.GenerateProof();
            var verified = challenge.Verify(now, crypto.HashProof(challenge.Id, challenge.NormalizedTarget, proof), settings.Policy.ProofLifetimeSeconds);
            return verified.IsSuccess ? Result<VerifyPhoneResponse>.Success(new VerifyPhoneResponse(proof, challenge.ProofExpiresAtUtc!.Value))
                : Result<VerifyPhoneResponse>.Failure(verified.Error);
        }, cancellationToken);
        if (result.IsSuccess)
        {
            // Credential state is authoritative; mailbox cleanup cannot grant a second verification.
            try { await sender.RemoveAsync(id, cancellationToken); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return result;
    }
}
