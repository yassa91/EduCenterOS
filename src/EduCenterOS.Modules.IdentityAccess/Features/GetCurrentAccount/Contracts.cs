namespace EduCenterOS.Modules.IdentityAccess.Features.GetCurrentAccount;

internal sealed record CurrentAccountResponse(Guid UserAccountId, Guid PersonIdentityId, string FullName, DateTimeOffset CreatedAtUtc);
