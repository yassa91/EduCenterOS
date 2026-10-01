using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Delivery;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
namespace EduCenterOS.Modules.IdentityAccess.Features.RequestPhoneVerification;

internal sealed class RequestPhoneVerificationHandler(RegistrationTransactions transactions, IDbContextFactory<IdentityAccessDbContext> factory,
    IdentityAccessRuntimeSettings settings, RegistrationCryptography crypto, IOtpSender sender)
{
    internal Task<Result<PhoneVerificationResponse>> HandleAsync(string? input, CancellationToken cancellationToken)
    {
        var phone = RegistrationInputs.Phone(input);
        return phone.IsSuccess ? IssueAsync(phone.Value, null, cancellationToken)
            : Task.FromResult(Result<PhoneVerificationResponse>.Failure(phone.Error));
    }
    internal async Task<Result<PhoneVerificationResponse>> IssueAsync(string phone, Guid? resendOf, CancellationToken cancellationToken)
    {
        var policy = settings.Policy;
        var reservation = await transactions.RunAsync(RegistrationOperation.Issue, phone, async (context, now, token) =>
        {
            if (resendOf is { } oldId)
            {
                var old = await context.Challenges.SingleOrDefaultAsync(value => value.Id == oldId, token);
                if (old is null || old.NormalizedTarget != phone || old.Purpose != "RegisterAccount" || old.Status is ChallengeStatus.Consumed or ChallengeStatus.Invalidated)
                    return Result<Reservation>.Failure(RegistrationErrors.VerificationRejected);
            }
            var target = await RegistrationTransactions.TargetAsync(context, crypto.TargetDigest(phone), token);
            var quota = target.ReserveIssue(now, policy.RollingWindowSeconds, policy.TargetIssuePermits, policy.MinimumResendSeconds);
            if (!quota.IsSuccess) return Result<Reservation>.Failure(quota.Error);
            var previous = await context.Challenges.Where(value => value.NormalizedTarget == phone && value.Purpose == "RegisterAccount"
                && (value.Status == ChallengeStatus.Active || value.Status == ChallengeStatus.Verified)).ToListAsync(token);
            foreach (var old in previous) old.Invalidate();
            // Persist invalidations before INSERT so the partial unique index never observes two live rows.
            await context.SaveChangesAsync(token);
            var id = Guid.CreateVersion7(now); var code = crypto.GenerateCode();
            var challenge = new OtpChallenge(id, phone, crypto.HashCode(id, phone, code, crypto.CurrentKeyVersion), crypto.CurrentKeyVersion, now, policy.CodeLifetimeSeconds);
            context.Challenges.Add(challenge);
            return Result<Reservation>.Success(new Reservation(new OtpMessage(id, code, challenge.ExpiresAtUtc), now.AddSeconds(policy.MinimumResendSeconds), previous.Select(value => value.Id).Concat(resendOf is { } requestedId ? [requestedId] : Array.Empty<Guid>()).Distinct().ToArray()));
        }, cancellationToken);
        if (!reservation.IsSuccess) return Result<PhoneVerificationResponse>.Failure(reservation.Error);
        var issued = reservation.Value;
        try
        {
            foreach (var oldId in issued.ReplacedIds) await sender.RemoveAsync(oldId, cancellationToken);
            await sender.DeliverAsync(issued.Message, cancellationToken);
            await using var check = await factory.CreateDbContextAsync(cancellationToken);
            var state = await check.Challenges.AsNoTracking().SingleOrDefaultAsync(value => value.Id == issued.Message.ChallengeId, cancellationToken);
            if (state is null || state.Status != ChallengeStatus.Active)
            {
                await sender.RemoveAsync(issued.Message.ChallengeId, cancellationToken);
                return Result<PhoneVerificationResponse>.Failure(RegistrationErrors.VerificationRejected);
            }
            return Result<PhoneVerificationResponse>.Success(new PhoneVerificationResponse(issued.Message.ChallengeId, issued.Message.ExpiresAtUtc, issued.ResendAvailableAtUtc));
        }
        catch (Exception exception) when (exception is OtpDeliveryException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await transactions.RunAsync(RegistrationOperation.Issue, phone, async (context, now, token) =>
                {
                    var row = await context.Challenges.SingleOrDefaultAsync(value => value.Id == issued.Message.ChallengeId, token);
                    row?.Invalidate(); return Result<bool>.Success(true);
                }, cleanup.Token);
                await sender.RemoveAsync(issued.Message.ChallengeId, cleanup.Token);
            }
            catch (Exception) { /* Reservation budget remains durable even when cleanup cannot reach the dependency. */ }
            if (exception is OperationCanceledException) throw;
            return Result<PhoneVerificationResponse>.Failure(new Error("Infrastructure.DeliveryUnavailable", ErrorCategory.ServiceUnavailable, "Verification delivery is temporarily unavailable."));
        }
    }
    private sealed class Reservation(OtpMessage message, DateTimeOffset resendAvailableAtUtc, Guid[] replacedIds)
    {
        internal OtpMessage Message { get; } = message;
        internal DateTimeOffset ResendAvailableAtUtc { get; } = resendAvailableAtUtc;
        internal Guid[] ReplacedIds { get; } = replacedIds;
    }
}
