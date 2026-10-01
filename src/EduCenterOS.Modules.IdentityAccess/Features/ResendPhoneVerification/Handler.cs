using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Features.RequestPhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EduCenterOS.Modules.IdentityAccess.Features.ResendPhoneVerification;

internal sealed class ResendPhoneVerificationHandler(IDbContextFactory<IdentityAccessDbContext> factory, RequestPhoneVerificationHandler issue)
{
    internal async Task<Result<PhoneVerificationResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var source = await context.Challenges.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        return source is null ? Result<PhoneVerificationResponse>.Failure(RegistrationErrors.VerificationRejected)
            : await issue.IssueAsync(source.NormalizedTarget, id, cancellationToken);
    }
}
internal sealed class ResendPhoneVerificationRequest;
