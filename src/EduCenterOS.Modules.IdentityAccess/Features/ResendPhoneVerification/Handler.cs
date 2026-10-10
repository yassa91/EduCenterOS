using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Features.Shared.PhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Features.ResendPhoneVerification;

internal sealed class ResendPhoneVerificationHandler(IDbContextFactory<IdentityAccessDbContext> factory, PhoneVerificationIssuance issuance)
{
    internal async Task<Result<PhoneVerificationResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var phone = await ChallengeQueries.TargetFor(context, id).SingleOrDefaultAsync(cancellationToken);

        return phone is null ? Result<PhoneVerificationResponse>.Failure(RegistrationErrors.VerificationRejected)
            : await issuance.IssueAsync(phone, id, cancellationToken);
    }
}
