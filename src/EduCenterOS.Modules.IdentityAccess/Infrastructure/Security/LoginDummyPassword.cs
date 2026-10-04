using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;

internal sealed class LoginDummyPassword
{
    public LoginDummyPassword(IdentityAccessRuntimeSettings settings)
    {
        var hasher = new PasswordHasher<UserAccount>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = settings.Policy.PasswordIterations
        }));
        Hash = hasher.HashPassword(null!, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    internal string Hash { get; }
}
