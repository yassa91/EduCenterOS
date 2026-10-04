using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Features.Login;

internal sealed class LoginHandler(
    AuthenticationTransactions transactions,
    IDbContextFactory<IdentityAccessDbContext> factory,
    AuthenticationRuntimeSettings settings,
    RegistrationCryptography crypto,
    AuthenticationTokens tokens,
    LoginDummyPassword dummy,
    IClock clock,
    IPasswordHasher<UserAccount> hasher
)
{
    internal async Task<Result<AuthenticationGrant>> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var phone = RegistrationInputs.Phone(request.PhoneNumber);
        var issues = new List<ValidationIssue>();

        if (!phone.IsSuccess) issues.AddRange(phone.Error.ValidationIssues);

        if (!RegistrationInputs.Password(request.Password, 1, 128)) issues.AddRange(RegistrationErrors.Field("password").ValidationIssues);

        if (issues.Count > 0) return Result<AuthenticationGrant>.Failure(Error.Validation(issues));

        var budget = await ReserveBudgetAsync(phone.Value, cancellationToken);

        if (!budget.IsSuccess) return Result<AuthenticationGrant>.Failure(budget.Error);

        await using var lookup = await factory.CreateDbContextAsync(cancellationToken);
        var candidate = await lookup.Accounts.AsNoTracking().SingleOrDefaultAsync(value => value.NormalizedPhoneNumber == phone.Value, cancellationToken);
        // Both unknown and existing accounts perform the reviewed password primitive outside row locks.
        var verification = hasher.VerifyHashedPassword(candidate!, candidate?.PasswordHash ?? dummy.Hash, request.Password!);

        if (verification is not (PasswordVerificationResult.Failed or PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
            throw new InvalidOperationException("Security.InvalidPasswordVerificationResult");

        if (candidate is null) return Result<AuthenticationGrant>.Failure(AuthenticationErrors.Rejected);

        var rehashed = verification == PasswordVerificationResult.SuccessRehashNeeded ? hasher.HashPassword(candidate, request.Password!) : candidate.PasswordHash;

        return await transactions.RunAsync(async (context, token) =>
        {
            var account = await AuthenticationTransactions.LockAccountAsync(context, candidate.Id, token);
            var now = clock.UtcNow;

            if (account is null || account.PasswordHash != candidate.PasswordHash || account.SecurityVersion != candidate.SecurityVersion || !account.CanAuthenticate(now))
                return Result<AuthenticationGrant>.Failure(AuthenticationErrors.Rejected);

            if (verification == PasswordVerificationResult.Failed)
            {
                account.FailAuthentication(now, settings.Policy.LockoutAttempts, settings.Policy.LockoutSeconds);

                return Result<AuthenticationGrant>.Failure(AuthenticationErrors.Rejected);
            }

            account.CompleteAuthentication(now, rehashed);
            var session = new UserSession(Guid.CreateVersion7(now), account.Id, account.SecurityVersion, now,
                settings.Policy.IdleTimeoutSeconds, settings.Policy.AbsoluteLifetimeSeconds);
            var raw = AuthenticationTokens.NewRefresh();
            var credential = new RefreshTokenRecord(Guid.CreateVersion7(now), session.Id, AuthenticationTokens.HashRefresh(raw), now, session.EffectiveExpiration);
            context.Sessions.Add(session);
            context.RefreshTokens.Add(credential);
            var access = tokens.Issue(session, now);

            return Result<AuthenticationGrant>.Success(new AuthenticationGrant(access, raw, credential.ExpiresAtUtc));
        }, cancellationToken);
    }

    private async Task<Result<bool>> ReserveBudgetAsync(string phone, CancellationToken cancellationToken)
    {
        var digest = crypto.LoginDigest(phone);

        return await transactions.RunAsync(async (context, token) =>
        {
            var admittedAt = clock.UtcNow;
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO identity_access.login_targets(digest,attempts_utc,last_attempt_at_utc) VALUES({digest},ARRAY[]::timestamptz[],{admittedAt}) ON CONFLICT DO NOTHING", token);
            var target = await context.LoginTargets.FromSqlInterpolated($"SELECT * FROM identity_access.login_targets WHERE digest={digest} FOR UPDATE").SingleAsync(token);
            var now = clock.UtcNow;
            var delay = target.Reserve(now, settings.Policy.LoginWindowSeconds, settings.Policy.LoginIdentifierPermits);

            return delay is null ? Result<bool>.Success(true) : Result<bool>.Failure(AuthenticationErrors.Throttled(delay));
        }, cancellationToken);
    }
}
