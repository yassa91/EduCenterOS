using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;

internal sealed class CurrentSessionMiddleware(RequestDelegate next, Func<HttpContext, Error, Task> writeError)
{
    public async Task InvokeAsync(HttpContext context, IDbContextFactory<IdentityAccessDbContext> factory, IClock clock)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<SecurityEndpointMetadata>()?.Classification == "AccountSelf")
        {
            if (
                !context.Request.IsHttps ||
                context.User.Identity?.IsAuthenticated != true ||
                context.Items[CurrentAccountActor.CryptographicItem] is not AuthenticatedActor actor
            )
            {
                await writeError(context, AuthenticationErrors.Rejected);

                return;
            }

            await using var database = await factory.CreateDbContextAsync(context.RequestAborted);
            var state = await database.Sessions.AsNoTracking().Where(session => session.Id == actor.SessionId)
                .Join(database.Accounts.AsNoTracking(), session => session.UserAccountId, account => account.Id, (session, account) => new { Session = session, Account = account })
                .SingleOrDefaultAsync(context.RequestAborted);

            // Infrastructure failures deliberately propagate to the outer safe error boundary.
            if (state is null || !AuthenticationState.AcceptsActor(state.Account, state.Session, actor, clock.UtcNow))
            {
                await writeError(context, AuthenticationErrors.Rejected);

                return;
            }

            context.Items[CurrentAccountActor.AuthoritativeItem] = actor;
        }

        await next(context);
    }
}
