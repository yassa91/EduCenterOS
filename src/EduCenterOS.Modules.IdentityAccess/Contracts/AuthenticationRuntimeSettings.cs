using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Configuration;

namespace EduCenterOS.Modules.IdentityAccess.Contracts;

// Secret-bearing settings deliberately have no record-generated ToString/serialization contract.
public sealed class AuthenticationRuntimeSettings
{
    private AuthenticationRuntimeSettings()
    {
    }

    internal AuthenticationPolicy Policy { get; private init; } = new();
    internal string PrivateKeyPem { get; private init; } = string.Empty;
    internal string CurrentKeyId { get; private init; } = string.Empty;
    internal IReadOnlyDictionary<string, string> ValidationPublicKeys { get; private init; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public void ValidateDevelopmentHostPort(int httpsPort)
    {
        if (new Uri(Policy.BrowserOrigin).Port != httpsPort)
            throw new InvalidOperationException("Configuration.AuthenticationOriginPortMismatch");
    }

    public static AuthenticationRuntimeSettings FromSnapshot(string policyJson, string privateKeyPem, string currentKeyId, IReadOnlyDictionary<string, string> publicKeys)
    {
        try
        {
            if (
                string.IsNullOrEmpty(privateKeyPem) || privateKeyPem.Length > 8192 || !privateKeyPem.StartsWith("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal) ||
                publicKeys is null || string.IsNullOrEmpty(currentKeyId) || publicKeys.Count is < 1 or > 4 || !publicKeys.ContainsKey(currentKeyId) ||
                publicKeys.Keys.Any(key => !Regex.IsMatch(key, "^[a-z][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant))
            )
                throw new InvalidOperationException();

            using var privateKey = RSA.Create();
            privateKey.ImportFromPem(privateKeyPem);

            if (privateKey.KeySize < 2048 || privateKey.KeySize > 4096) throw new InvalidOperationException();

            foreach (var pair in publicKeys)
            {
                if (string.IsNullOrEmpty(pair.Value) || pair.Value.Length > 8192 || !pair.Value.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
                    throw new InvalidOperationException();

                using var publicKey = RSA.Create();
                publicKey.ImportFromPem(pair.Value);

                if (publicKey.KeySize < 2048 || publicKey.KeySize > 4096) throw new InvalidOperationException();

                if (pair.Key == currentKeyId && !CryptographicOperations.FixedTimeEquals(privateKey.ExportSubjectPublicKeyInfo(), publicKey.ExportSubjectPublicKeyInfo()))
                    throw new InvalidOperationException();
            }

            return new AuthenticationRuntimeSettings
            {
                Policy = AuthenticationPolicy.Parse(policyJson),
                PrivateKeyPem = privateKeyPem,
                CurrentKeyId = currentKeyId,
                ValidationPublicKeys = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(publicKeys, StringComparer.Ordinal))
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or CryptographicException or FormatException)
        {
            throw new InvalidOperationException("Configuration.InvalidAuthenticationSettings");
        }
    }
}
