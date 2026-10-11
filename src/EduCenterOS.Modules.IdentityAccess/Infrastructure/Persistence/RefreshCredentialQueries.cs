using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;

internal sealed record RefreshCredentialOwner(Guid CredentialId, Guid SessionId, Guid AccountId);

internal static class RefreshCredentialQueries
{
    // Locates the rows to lock; the transaction still validates ownership and the credential hash.
    internal static IQueryable<RefreshCredentialOwner> OwnerForHash(IdentityAccessDbContext context, byte[] hash) =>
        context.RefreshTokens.AsNoTracking()
            .Where(credential => credential.TokenHash == hash)
            .Join(context.Sessions.AsNoTracking(), credential => credential.UserSessionId, session => session.Id,
                (credential, session) => new RefreshCredentialOwner(credential.Id, session.Id, session.UserAccountId));
}
