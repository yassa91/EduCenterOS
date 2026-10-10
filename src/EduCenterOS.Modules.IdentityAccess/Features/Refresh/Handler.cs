using System.Security.Cryptography;
using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Features.Shared.Authentication;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Features.Refresh;

internal sealed class RefreshHandler(
    AuthenticationTransactions transactions,
    IDbContextFactory<IdentityAccessDbContext> factory,
    AuthenticationRuntimeSettings settings,
    AuthenticationTokens tokens,
    IClock clock
)
{
    internal async Task<Result<AuthenticationGrant>> HandleAsync(string? raw, CancellationToken cancellationToken)
    {
        if (!RegistrationCryptography.IsProof(raw)) return Result<AuthenticationGrant>.Failure(AuthenticationErrors.Rejected);

        var hash = AuthenticationTokens.HashRefresh(raw!);
        await using var lookup = await factory.CreateDbContextAsync(cancellationToken);
        var candidate = await RefreshCredentialQueries.OwnerForHash(lookup, hash)
            .SingleOrDefaultAsync(cancellationToken);

        if (candidate is null) return Result<AuthenticationGrant>.Failure(AuthenticationErrors.Rejected);

        return await transactions.RunAsync(async (context, token) =>
        {
            var account = await AuthenticationTransactions.LockAccountAsync(context, candidate.AccountId, token);

            if (account is null) return Result<AuthenticationGrant>.Failure(AuthenticationErrors.Rejected);

            var session = await AuthenticationTransactions.LockSessionAsync(context, candidate.SessionId, token);

            if (session is null || session.UserAccountId != account.Id) return Result<AuthenticationGrant>.Failure(AuthenticationErrors.Rejected);

            var credential = await AuthenticationTransactions.LockRefreshAsync(context, candidate.CredentialId, token);
            var now = clock.UtcNow;

            if (
                credential is null ||
                credential.UserSessionId != session.Id ||
                !CryptographicOperations.FixedTimeEquals(credential.TokenHash, hash)
            )
                return Result<AuthenticationGrant>.Failure(AuthenticationErrors.Rejected);

            if (credential.ConsumedAtUtc is not null)
            {
                // Reuse is a committed security outcome, including an expired ancestor.
                if (session.RevokedAtUtc is null && now >= session.CreatedAtUtc) session.Revoke(now, "RefreshReuse");

                return Result<AuthenticationGrant>.Failure(AuthenticationErrors.Rejected);
            }

            if (
                !SessionAccessState.From(account, session).OwnsLiveSession(now) ||
                !credential.CanConsume(now) ||
                now < session.LastSeenAtUtc ||
                (DateTimeOffset.FromUnixTimeSeconds(session.AbsoluteExpiresAtUtc.ToUnixTimeSeconds()) - now).TotalSeconds < 1
            )
                return Result<AuthenticationGrant>.Failure(AuthenticationErrors.Rejected);

            session.RefreshActivity(now, settings.Policy.IdleTimeoutSeconds);
            var replacementRaw = AuthenticationTokens.NewRefresh();
            var replacement = new RefreshTokenRecord(Guid.CreateVersion7(now), session.Id,
                AuthenticationTokens.HashRefresh(replacementRaw), now, session.EffectiveExpiration);
            context.RefreshTokens.Add(replacement);
            credential.Consume(now, replacement.Id);
            var access = tokens.Issue(session, now);

            return Result<AuthenticationGrant>.Success(new AuthenticationGrant(access, replacementRaw, replacement.ExpiresAtUtc));
        }, cancellationToken);
    }
}
