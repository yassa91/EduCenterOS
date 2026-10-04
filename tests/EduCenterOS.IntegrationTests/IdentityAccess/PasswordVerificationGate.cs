using EduCenterOS.Modules.IdentityAccess.Domain;
using Microsoft.AspNetCore.Identity;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

internal sealed class PasswordVerificationGate(IPasswordHasher<UserAccount> inner) : IPasswordHasher<UserAccount>
{
    internal bool Enabled { get; set; }
    internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string HashPassword(UserAccount user, string password) => inner.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(UserAccount user, string hashedPassword, string providedPassword)
    {
        var verified = inner.VerifyHashedPassword(user, hashedPassword, providedPassword);

        if (Enabled)
        {
            Reached.TrySetResult();
            Release.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        }

        return verified;
    }
}
