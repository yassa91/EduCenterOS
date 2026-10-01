using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Configuration;
using Npgsql;
namespace EduCenterOS.Modules.IdentityAccess.Contracts;

// Deliberately not a record: diagnostic ToString must never dump credential properties.
public sealed class IdentityAccessRuntimeSettings
{
    private IdentityAccessRuntimeSettings() { }
    internal string ConnectionString { get; private init; } = string.Empty;
    internal IReadOnlyDictionary<string, byte[]> OtpKeys { get; private init; } = new ReadOnlyDictionary<string, byte[]>(new Dictionary<string, byte[]>());
    internal string CurrentKeyVersion { get; private init; } = string.Empty;
    internal byte[] PartitionKey { get; private init; } = [];
    internal RegistrationSecurityOptions Policy { get; private init; } = new();
    internal string? MailboxDirectory { get; private init; }

    public static IdentityAccessRuntimeSettings FromSnapshot(string connectionString, IReadOnlyDictionary<string, string> otpKeys,
        string currentVersion, string partitionKey, string policyJson, string environment, string? mailboxDirectory)
    {
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(connectionString);
            if (connection.Host is not ("127.0.0.1" or "localhost") || connection.Port is < 1 or > 65535
                || connection.Username != "educenteros_identity_runtime" || string.IsNullOrEmpty(connection.Password)
                || connection.IncludeErrorDetail || (environment == "Development" && connection.Database != "educenteros_dev")
                || (environment == "Testing" && !(connection.Database?.EndsWith("_tests", StringComparison.Ordinal) ?? false))
                || environment is not ("Development" or "Testing")) throw new InvalidOperationException();
            if (otpKeys.Count is < 1 or > 4 || !otpKeys.ContainsKey(currentVersion)
                || otpKeys.Keys.Select(key => key.ToUpperInvariant()).Distinct().Count() != otpKeys.Count) throw new InvalidOperationException();
            var material = otpKeys.ToDictionary(pair => pair.Key, pair => DecodeKey(pair.Value), StringComparer.Ordinal);
            if (material.Keys.Any(version => !Regex.IsMatch(version, @"^[A-Za-z0-9_-]{1,32}\z", RegexOptions.CultureInvariant))) throw new InvalidOperationException();
            var partition = DecodeKey(partitionKey);
            if (material.Values.Any(key => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(key, partition))) throw new InvalidOperationException();
            if (environment == "Testing" && mailboxDirectory is not null) throw new InvalidOperationException();
            if (environment == "Development")
            {
                if (mailboxDirectory is null || !Path.IsPathFullyQualified(mailboxDirectory) || Path.GetFullPath(mailboxDirectory) != mailboxDirectory
                    || Path.GetFileName(mailboxDirectory) != "otp" || Path.GetFileName(Path.GetDirectoryName(mailboxDirectory)) != ".local"
                    || !File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(mailboxDirectory))!, "EduCenterOS.sln"))) throw new InvalidOperationException();
            }
            return new IdentityAccessRuntimeSettings { ConnectionString = connection.ConnectionString,
                OtpKeys = new ReadOnlyDictionary<string, byte[]>(material), CurrentKeyVersion = currentVersion, PartitionKey = partition,
                Policy = RegistrationSecurityOptions.Parse(policyJson), MailboxDirectory = mailboxDirectory };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or FormatException or IOException or OverflowException)
        { throw new InvalidOperationException("Configuration.InvalidIdentityAccessSettings: complete validated module configuration is required."); }
    }
    private static byte[] DecodeKey(string value)
    {
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length is < 32 or > 64 || Convert.ToBase64String(bytes) != value) throw new InvalidOperationException();
        return bytes;
    }
}
