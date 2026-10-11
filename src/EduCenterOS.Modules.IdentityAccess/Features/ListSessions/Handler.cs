using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Features.ListSessions;

internal sealed class ListSessionsHandler(ICurrentAccountActor current, IDbContextFactory<IdentityAccessDbContext> factory)
{
    internal async Task<Result<SessionPageResponse>> HandleAsync(SessionPage page, CancellationToken cancellationToken)
    {
        var actor = current.Require();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var count = await context.Sessions.AsNoTracking().LongCountAsync(session => session.UserAccountId == actor.AccountId, cancellationToken);
        var items = await SessionHistoryQueries.PageFor(context, actor, page).ToArrayAsync(cancellationToken);
        var pages = count / page.PageSize + (count % page.PageSize == 0 ? 0 : 1);

        return Result<SessionPageResponse>.Success(new SessionPageResponse(
            items,
            new SessionPagination("page", page.Page, page.PageSize, count, pages)
        ));
    }
}
