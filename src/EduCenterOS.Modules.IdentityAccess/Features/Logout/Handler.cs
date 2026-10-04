using System.Security.Cryptography;
using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Features.Logout;

internal sealed class LogoutHandler(AuthenticationTransactions transactions, IDbContextFactory<IdentityAccessDbContext> factory, IClock clock)
{
    internal async Task<Result<bool>> HandleAsync(string? raw, CancellationToken cancellationToken)
    {
        if (!RegistrationCryptography.IsProof(raw)) return Result<bool>.Success(true);

        var hash = AuthenticationTokens.HashRefresh(raw!);
        await using var lookup = await factory.CreateDbContextAsync(cancellationToken);
        var candidate = await lookup.RefreshTokens.AsNoTracking().Where(credential => credential.TokenHash == hash)
            .Join(lookup.Sessions.AsNoTracking(), credential => credential.UserSessionId, session => session.Id,
                (credential, session) => new { CredentialId = credential.Id, SessionId = session.Id, AccountId = session.UserAccountId })
            .SingleOrDefaultAsync(cancellationToken);

        if (candidate is null) return Result<bool>.Success(true);

        return await transactions.RunAsync(async (context, token) =>
        {
            var account = await AuthenticationTransactions.LockAccountAsync(context, candidate.AccountId, token);

            if (account is null) return Result<bool>.Success(true);

            var session = await AuthenticationTransactions.LockSessionAsync(context, candidate.SessionId, token);

            if (session is null || session.UserAccountId != account.Id) return Result<bool>.Success(true);

            var credential = await AuthenticationTransactions.LockRefreshAsync(context, candidate.CredentialId, token);
            var now = clock.UtcNow;

            if (
                credential is null ||
                credential.UserSessionId != session.Id ||
                !CryptographicOperations.FixedTimeEquals(credential.TokenHash, hash)
            )
                return Result<bool>.Success(true);

            session.Revoke(now, "LogoutCurrent");

            return Result<bool>.Success(true);
        }, cancellationToken);
    }
}
