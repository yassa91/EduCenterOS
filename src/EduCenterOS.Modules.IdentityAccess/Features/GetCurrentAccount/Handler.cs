using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Features.GetCurrentAccount;

internal sealed record CurrentAccountResponse(Guid UserAccountId, Guid PersonIdentityId, string FullName, DateTimeOffset CreatedAtUtc);

internal sealed class GetCurrentAccountHandler(ICurrentAccountActor current, IDbContextFactory<IdentityAccessDbContext> factory)
{
    internal async Task<Result<CurrentAccountResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var actor = current.Require();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var response = await context.Accounts.AsNoTracking().Where(account => account.Id == actor.AccountId)
            .Join(context.People.AsNoTracking(), account => account.PersonIdentityId, person => person.Id,
                (account, person) => new CurrentAccountResponse(account.Id, person.Id, person.FullName, account.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        return response is null ? Result<CurrentAccountResponse>.Failure(AuthenticationErrors.Rejected) : Result<CurrentAccountResponse>.Success(response);
    }
}
