using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Features.Shared.PhoneVerification;

namespace EduCenterOS.Modules.IdentityAccess.Features.RequestPhoneVerification;

internal sealed class RequestPhoneVerificationHandler(PhoneVerificationIssuance issuance)
{
    internal Task<Result<PhoneVerificationResponse>> HandleAsync(string? input, CancellationToken cancellationToken)
    {
        var phone = RegistrationInputs.Phone(input);

        return phone.IsSuccess
            ? issuance.IssueAsync(phone.Value, null, cancellationToken)
            : Task.FromResult(Result<PhoneVerificationResponse>.Failure(phone.Error));
    }
}
