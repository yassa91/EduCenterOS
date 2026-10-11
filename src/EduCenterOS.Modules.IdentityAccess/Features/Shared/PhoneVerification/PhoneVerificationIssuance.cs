using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Delivery;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduCenterOS.Modules.IdentityAccess.Features.Shared.PhoneVerification;

internal sealed class PhoneVerificationIssuance(
    RegistrationTransactions transactions,
    IDbContextFactory<IdentityAccessDbContext> factory,
    IdentityAccessRuntimeSettings settings,
    RegistrationCryptography crypto,
    IOtpSender sender,
    ILogger<PhoneVerificationIssuance> logger
)
{
    internal async Task<Result<PhoneVerificationResponse>> IssueAsync(string phone, Guid? resendOf, CancellationToken cancellationToken)
    {
        var reservation = await ReserveAsync(phone, resendOf, cancellationToken);

        if (!reservation.IsSuccess) return Result<PhoneVerificationResponse>.Failure(reservation.Error);

        return await DeliverAsync(phone, reservation.Value, cancellationToken);
    }

    private Task<Result<Reservation>> ReserveAsync(string phone, Guid? resendOf, CancellationToken cancellationToken)
    {
        var policy = settings.Policy;

        return transactions.RunAsync(RegistrationOperation.Issue, phone, async (context, now, token) =>
        {
            if (resendOf is { } oldId)
            {
                var old = await context.Challenges.SingleOrDefaultAsync(value => value.Id == oldId, token);

                if (
                    old is null ||
                    old.NormalizedTarget != phone ||
                    old.Purpose != "RegisterAccount" ||
                    old.Status is ChallengeStatus.Consumed or ChallengeStatus.Invalidated
                )
                    return Result<Reservation>.Failure(RegistrationErrors.VerificationRejected);
            }

            var target = await RegistrationTransactions.TargetAsync(context, crypto.TargetDigest(phone), token);
            var quota = target.ReserveIssue(now, policy.RollingWindowSeconds, policy.TargetIssuePermits, policy.MinimumResendSeconds);

            if (!quota.IsSuccess) return Result<Reservation>.Failure(quota.Error);

            var previous = await context.Challenges
                .Where(value => value.NormalizedTarget == phone && value.Purpose == "RegisterAccount" &&
                    (value.Status == ChallengeStatus.Active || value.Status == ChallengeStatus.Verified))
                .ToListAsync(token);

            foreach (var old in previous) old.Invalidate();

            // Persist invalidations before INSERT so the partial unique index never observes two live rows.
            await context.SaveChangesAsync(token);

            var id = Guid.CreateVersion7(now);
            var code = crypto.GenerateCode();
            var challenge = new OtpChallenge(
                id, phone, crypto.HashCode(id, phone, code, crypto.CurrentKeyVersion),
                crypto.CurrentKeyVersion, now, policy.CodeLifetimeSeconds
            );
            context.Challenges.Add(challenge);

            var replacedIds = previous.Select(value => value.Id)
                .Concat(resendOf is { } requestedId ? [requestedId] : Array.Empty<Guid>())
                .Distinct()
                .ToArray();
            var message = new OtpMessage(id, code, challenge.ExpiresAtUtc);
            var resendAvailableAt = now.AddSeconds(policy.MinimumResendSeconds);

            return Result<Reservation>.Success(new Reservation(message, resendAvailableAt, replacedIds));
        }, cancellationToken);
    }

    private async Task<Result<PhoneVerificationResponse>> DeliverAsync(string phone, Reservation issued, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var oldId in issued.ReplacedIds) await sender.RemoveAsync(oldId, cancellationToken);

            await sender.DeliverAsync(issued.Message, cancellationToken);
            await using var check = await factory.CreateDbContextAsync(cancellationToken);
            var active = await ChallengeQueries.Active(check, issued.Message.ChallengeId).AnyAsync(cancellationToken);

            if (!active)
            {
                await sender.RemoveAsync(issued.Message.ChallengeId, cancellationToken);

                return Result<PhoneVerificationResponse>.Failure(RegistrationErrors.VerificationRejected);
            }

            return Result<PhoneVerificationResponse>.Success(new PhoneVerificationResponse(
                issued.Message.ChallengeId,
                issued.Message.ExpiresAtUtc,
                issued.ResendAvailableAtUtc
            ));
        }
        catch (Exception exception) when (exception is OtpDeliveryException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            await CleanupFailedDeliveryAsync(phone, issued);

            if (exception is OperationCanceledException) throw;

            return Result<PhoneVerificationResponse>.Failure(new Error(
                "Infrastructure.DeliveryUnavailable",
                ErrorCategory.ServiceUnavailable,
                "Verification delivery is temporarily unavailable."
            ));
        }
    }

    private async Task CleanupFailedDeliveryAsync(string phone, Reservation issued)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await transactions.RunAsync(RegistrationOperation.Issue, phone, async (context, now, token) =>
            {
                var row = await context.Challenges.SingleOrDefaultAsync(value => value.Id == issued.Message.ChallengeId, token);
                row?.Invalidate();

                return Result<bool>.Success(true);
            }, cleanup.Token);
            await sender.RemoveAsync(issued.Message.ChallengeId, cleanup.Token);
        }
        catch (Exception)
        {
            // Reservation budget remains durable; logs must never carry codes, proofs, or targets.
            logger.LogWarning(new EventId(1101, "VerificationDeliveryCleanupUnavailable"), "Verification delivery cleanup is temporarily unavailable.");
        }
    }

    private sealed class Reservation(OtpMessage message, DateTimeOffset resendAvailableAtUtc, Guid[] replacedIds)
    {
        internal OtpMessage Message { get; } = message;
        internal DateTimeOffset ResendAvailableAtUtc { get; } = resendAvailableAtUtc;
        internal Guid[] ReplacedIds { get; } = replacedIds;
    }
}
