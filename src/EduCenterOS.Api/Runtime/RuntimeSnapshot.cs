using System.Text;
using System.Text.Json;
using Npgsql;

namespace EduCenterOS.Api.Runtime;

internal sealed class RuntimeSnapshot
{
    private RuntimeSnapshot(string connectionString) => ConnectionString = connectionString;

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
            RequireKeys(root, ["schemaVersion", "environment", "source", "secrets"]);
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("environment").GetString() != environment)
                throw new InvalidOperationException();

            var expectedSource = environment switch
            {
                "Development" => "Infisical",
                "Testing" => "Synthetic",
                _ => throw new InvalidOperationException()
            };
            if (root.GetProperty("source").GetString() != expectedSource)
                throw new InvalidOperationException();

            var secrets = root.GetProperty("secrets");
            RequireKeys(secrets, ["ConnectionStrings__RuntimeProbeDatabase"]);
            var value = secrets.GetProperty("ConnectionStrings__RuntimeProbeDatabase").GetString();
            var connection = new NpgsqlConnectionStringBuilder(value);
            if (connection.Host is not ("127.0.0.1" or "localhost") || connection.Port is < 1 or > 65535
                || connection.Username != "educenteros_runtime_probe" || string.IsNullOrEmpty(connection.Password)
                || (environment == "Development" && connection.Database != "educenteros_dev")
                || (environment == "Testing" && !(connection.Database?.EndsWith("_tests", StringComparison.Ordinal) ?? false)))
                throw new InvalidOperationException();

            return new RuntimeSnapshot(connection.ConnectionString);
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
