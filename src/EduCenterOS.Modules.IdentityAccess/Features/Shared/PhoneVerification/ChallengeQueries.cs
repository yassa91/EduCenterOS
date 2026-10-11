using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Features.Shared.PhoneVerification;

internal static class ChallengeQueries
{
    // The transaction reloads authoritative state; this lookup only identifies its target lock.
    internal static IQueryable<string> TargetFor(IdentityAccessDbContext context, Guid challengeId) =>
        context.Challenges.AsNoTracking()
            .Where(challenge => challenge.Id == challengeId)
            .Select(challenge => challenge.NormalizedTarget);

    internal static IQueryable<OtpChallenge> Active(IdentityAccessDbContext context, Guid challengeId) =>
        context.Challenges.AsNoTracking()
            .Where(challenge => challenge.Id == challengeId && challenge.Status == ChallengeStatus.Active);
}
