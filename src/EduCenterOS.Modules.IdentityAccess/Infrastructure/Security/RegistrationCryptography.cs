using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Domain;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;

internal sealed class RegistrationCryptography(IdentityAccessRuntimeSettings settings)
{
    internal string CurrentKeyVersion => settings.CurrentKeyVersion;

    internal string GenerateCode() => RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6", CultureInfo.InvariantCulture);

    internal byte[] HashCode(Guid id, string phone, string code, string version)
    {
        if (!settings.OtpKeys.TryGetValue(version, out var key)) throw new InvalidOperationException("Security.UnknownOtpKeyVersion");

        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"otp\0{id:N}\0RegisterAccount\0{phone}\0{code}"));
    }

    internal bool MatchesCode(OtpChallenge challenge, string code) => settings.OtpKeys.ContainsKey(challenge.HashKeyVersion)
        && CryptographicOperations.FixedTimeEquals(challenge.CodeHash, HashCode(challenge.Id, challenge.NormalizedTarget, code, challenge.HashKeyVersion));

    internal string GenerateProof() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static bool IsProof(string? value) => value is { Length: 43 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
        && (value[^1] is 'A' or 'E' or 'I' or 'M' or 'Q' or 'U' or 'Y' or 'c' or 'g' or 'k' or 'o' or 's' or 'w' or '0' or '4' or '8');

    internal byte[] HashProof(Guid id, string phone, string proof) => SHA256.HashData(Encoding.UTF8.GetBytes($"proof\0{id:N}\0RegisterAccount\0{phone}\0{proof}"));

    internal byte[] TargetDigest(string phone) => HMACSHA256.HashData(settings.PartitionKey, Encoding.UTF8.GetBytes("target\0" + phone));

    internal byte[] LoginDigest(string phone) => HMACSHA256.HashData(settings.PartitionKey, Encoding.UTF8.GetBytes("login-target\0" + phone));

    internal long TargetLock(string phone) => BinaryPrimitives.ReadInt64BigEndian(TargetDigest(phone));

    internal int Bucket(string signal) => (int)(BinaryPrimitives.ReadUInt32BigEndian(HMACSHA256.HashData(settings.PartitionKey, Encoding.UTF8.GetBytes("ip\0" + signal))) % settings.Policy.BucketCount);
}
