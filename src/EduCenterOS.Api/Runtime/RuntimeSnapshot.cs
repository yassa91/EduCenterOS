using EduCenterOS.Modules.IdentityAccess.Contracts;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace EduCenterOS.Api.Runtime;

internal sealed class RuntimeSnapshot
{
    private RuntimeSnapshot(string connectionString, IdentityAccessRuntimeSettings identityAccess)
    { ConnectionString = connectionString; IdentityAccess = identityAccess; }

    internal IdentityAccessRuntimeSettings IdentityAccess { get; }

    internal string ConnectionString { get; }

    internal static RuntimeSnapshot Parse(string? json, string environment)
    {
        try
        {
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > 16384)
                throw new InvalidOperationException();

            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            RejectDuplicateProperties(document.RootElement);
            var root = document.RootElement;
            RequireKeys(root, ["schemaVersion", "environment", "source", "secrets", "securityPolicy", "developmentMailboxDirectory", "databaseTarget"]);
            if (root.GetProperty("schemaVersion").GetInt32() != 3 || root.GetProperty("environment").GetString() != environment)
                throw new InvalidOperationException();

            var expectedSource = environment switch
            {
                "Development" => "Infisical",
                "Testing" => "CloudTestFixture",
                _ => throw new InvalidOperationException()
            };
            if (root.GetProperty("source").GetString() != expectedSource)
                throw new InvalidOperationException();

            var secrets = root.GetProperty("secrets");
            var keyNames = secrets.EnumerateObject().Select(property => property.Name).ToArray();
            var otpKeys = keyNames.Where(key => key.StartsWith("IdentityAccess__Otp__HashKeys__", StringComparison.Ordinal)).ToArray();
            if (otpKeys.Length is < 1 or > 4) throw new InvalidOperationException();
            RequireKeys(secrets, ["ConnectionStrings__RuntimeProbeDatabase", "ConnectionStrings__IdentityAccessDatabase",
                "IdentityAccess__Otp__CurrentHashKeyVersion", "Platform__RateLimiting__PartitionDigestKey", .. otpKeys]);
            var targetJson = root.GetProperty("databaseTarget");
            RequireKeys(targetJson, ["projectReference", "host", "environment", "serverMajor"]);
            var target = JsonSerializer.Deserialize<SupabaseDatabaseTarget>(targetJson.GetRawText(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? throw new InvalidOperationException();
            var connection = target.Validate(secrets.GetProperty("ConnectionStrings__RuntimeProbeDatabase").GetString()!, environment, "probe");

            var moduleConnection = new NpgsqlConnectionStringBuilder(secrets.GetProperty("ConnectionStrings__IdentityAccessDatabase").GetString());
            if (moduleConnection.Host != connection.Host || moduleConnection.Port != connection.Port || moduleConnection.Database != connection.Database)
                throw new InvalidOperationException();
            var identityAccess = IdentityAccessRuntimeSettings.FromSnapshot(moduleConnection.ConnectionString,
                otpKeys.ToDictionary(key => key["IdentityAccess__Otp__HashKeys__".Length..], key => secrets.GetProperty(key).GetString()!),
                secrets.GetProperty("IdentityAccess__Otp__CurrentHashKeyVersion").GetString()!,
                secrets.GetProperty("Platform__RateLimiting__PartitionDigestKey").GetString()!, root.GetProperty("securityPolicy").GetRawText(),
                target, environment, root.GetProperty("developmentMailboxDirectory").ValueKind == JsonValueKind.Null ? null : root.GetProperty("developmentMailboxDirectory").GetString());
            return new RuntimeSnapshot(connection.ConnectionString, identityAccess);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidOperationException("Configuration.InvalidRuntimeSnapshot: a complete environment-bound startup snapshot is required.");
        }
    }

    private static void RequireKeys(JsonElement element, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException();
        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != expected.Length || actual.Except(expected, StringComparer.Ordinal).Any())
            throw new InvalidOperationException();
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new InvalidOperationException();
            RejectDuplicateProperties(property.Value);
        }
    }
}

internal interface IRuntimeSnapshotSource
{
    RuntimeSnapshot Read();
}

internal sealed class EnvironmentSnapshotSource(string environment) : IRuntimeSnapshotSource
{
    private readonly Lazy<RuntimeSnapshot> snapshot = new(() => RuntimeSnapshot.Parse(
        Environment.GetEnvironmentVariable("EDUCENTEROS_RUNTIME_SNAPSHOT"), environment));

    public RuntimeSnapshot Read() => snapshot.Value;
}
