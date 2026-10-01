using System.Text.Json.Nodes;
using EduCenterOS.Api.Infrastructure;
using Npgsql;
using Xunit;

namespace EduCenterOS.UnitTests;

public sealed class RuntimeSnapshotTests
{
    private const string SecretKey = "ConnectionStrings__RuntimeProbeDatabase";
    private const string Marker = "diagnostic-sensitive-marker";

    private static JsonObject Snapshot(string environment = "Testing") => new()
    {
        ["schemaVersion"] = 1,
        ["environment"] = environment,
        ["source"] = environment == "Testing" ? "Synthetic" : "Infisical",
        ["secrets"] = new JsonObject
        {
            [SecretKey] = $"Host=127.0.0.1;Port=55432;Database={(environment == "Testing" ? "owned_tests" : "educenteros_dev")};Username=educenteros_runtime_probe;Password={Marker}"
        }
    };

    [Theory]
    [InlineData("Testing")]
    [InlineData("Development")]
    public void Parse_CompleteEnvironmentBoundSnapshot_AcceptsExplicitSource(string environment)
    {
        var parsed = RuntimeSnapshot.Parse(Snapshot(environment).ToJsonString(), environment);
        var connection = new NpgsqlConnectionStringBuilder(parsed.ConnectionString);
        Assert.Equal("educenteros_runtime_probe", connection.Username);
        Assert.True(connection.Password == Marker, "The credential must be preserved without displaying it.");
    }

    [Theory]
    [InlineData("environment")]
    [InlineData("source")]
    [InlineData("schemaVersion")]
    [InlineData("extraRootKey")]
    [InlineData("extraSecret")]
    [InlineData("remoteHost")]
    [InlineData("wrongDatabase")]
    [InlineData("wrongRole")]
    [InlineData("missingPassword")]
    public void Parse_InvalidProtocolOrDestination_RejectsWithoutSecretDisclosure(string fault)
    {
        var value = Snapshot();
        switch (fault)
        {
            case "environment": value["environment"] = "Development"; break;
            case "source": value["source"] = "Infisical"; break;
            case "schemaVersion": value["schemaVersion"] = 2; break;
            case "extraRootKey": value["unexpected"] = Marker; break;
            case "extraSecret": value["secrets"]!["OtherSecret"] = Marker; break;
            default:
                var connection = new NpgsqlConnectionStringBuilder(value["secrets"]![SecretKey]!.GetValue<string>());
                if (fault == "remoteHost") connection.Host = "external.invalid";
                if (fault == "wrongDatabase") connection.Database = "educenteros_dev";
                if (fault == "wrongRole") connection.Username = "educenteros_admin";
                if (fault == "missingPassword") connection.Password = "";
                value["secrets"]![SecretKey] = connection.ConnectionString;
                break;
        }
        AssertSafeRejection(value.ToJsonString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    public void Parse_MissingOrMalformedSnapshot_Rejects(string? json) => AssertSafeRejection(json);

    [Fact]
    public void Parse_DuplicateCaseVariantProperty_Rejects()
        => AssertSafeRejection(Snapshot().ToJsonString().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"SchemaVersion\":1", StringComparison.Ordinal));

    [Fact]
    public void Parse_OversizedSnapshot_Rejects() => AssertSafeRejection(new string('x', 16385));

    private static void AssertSafeRejection(string? json)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RuntimeSnapshot.Parse(json, "Testing"));
        Assert.StartsWith("Configuration.InvalidRuntimeSnapshot", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.False(exception.ToString().Contains(Marker, StringComparison.Ordinal));
    }
}
