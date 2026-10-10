using EduCenterOS.UnitTests.Infrastructure;
using System.Text.Json.Nodes;
using EduCenterOS.Api.Runtime;
using Npgsql;
using Xunit;

namespace EduCenterOS.UnitTests;

public sealed class RuntimeSnapshotTests
{
    private const string SecretKey = "ConnectionStrings__RuntimeProbeDatabase";
    private const string Marker = "diagnostic-sensitive-marker";

    private static readonly TestRsaKey SigningKey = new();

    private static JsonObject Snapshot(string environment = "Testing") => new()
    {
        ["schemaVersion"] = 4,
        ["databaseTarget"] = new JsonObject { ["projectReference"] = "abcdefghijklmnopqrst", ["host"] = "db.abcdefghijklmnopqrst.supabase.co", ["environment"] = environment, ["serverMajor"] = 17 },
        ["securityPolicy"] = System.Text.Json.JsonSerializer.SerializeToNode(
            new EduCenterOS.Modules.IdentityAccess.Infrastructure.Configuration.RegistrationSecurityOptions(),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }
        ),
        ["authenticationPolicy"] = System.Text.Json.JsonSerializer.SerializeToNode(
            new EduCenterOS.Modules.IdentityAccess.Infrastructure.Configuration.AuthenticationPolicy(),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }
        ),
        ["developmentMailboxDirectory"] = environment == "Development" ? Path.Combine(Root(), ".local/otp") : null,
        ["environment"] = environment,
        ["source"] = environment == "Testing" ? "CloudTestFixture" : "Infisical",
        ["secrets"] = new JsonObject
        {
            ["ConnectionStrings__IdentityAccessDatabase"] = $"Host=db.abcdefghijklmnopqrst.supabase.co;Port=5432;Database=postgres;Username=educenteros_{(environment == "Testing" ? "test" : "dev")}_runtime;Password={Marker};SSL Mode=VerifyFull",
            ["IdentityAccess__Jwt__PrivateKeyPem"] = SigningKey.PrivatePem,
            ["IdentityAccess__Jwt__CurrentKeyId"] = "s03-test",
            ["IdentityAccess__Jwt__ValidationPublicKeys__s03-test"] = SigningKey.PublicPem,
            ["IdentityAccess__Otp__HashKeys__v1"] = Convert.ToBase64String(new byte[32]),
            ["IdentityAccess__Otp__CurrentHashKeyVersion"] = "v1",
            ["Platform__RateLimiting__PartitionDigestKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()),
            [SecretKey] = $"Host=db.abcdefghijklmnopqrst.supabase.co;Port=5432;Database=postgres;Username=educenteros_{(environment == "Testing" ? "test" : "dev")}_probe;Password={Marker};SSL Mode=VerifyFull"
        }
    };

    [Theory]
    [InlineData("Testing")]
    [InlineData("Development")]
    public void Parse_CompleteEnvironmentBoundSnapshot_AcceptsExplicitSource(string environment)
    {
        var parsed = RuntimeSnapshot.Parse(Snapshot(environment).ToJsonString(), environment);
        var connection = new NpgsqlConnectionStringBuilder(parsed.ConnectionString);
        Assert.Equal("educenteros_" + (environment == "Testing" ? "test" : "dev") + "_probe", connection.Username);
        Assert.True(connection.Password == Marker, "The credential must be preserved without displaying it.");
    }

    [Theory]
    [InlineData("environment")]
    [InlineData("source")]
    [InlineData("schemaVersion")]
    [InlineData("oldSchema")]
    [InlineData("missingSigningKey")]
    [InlineData("missingAuthPolicy")]
    [InlineData("extraRootKey")]
    [InlineData("extraSecret")]
    [InlineData("remoteHost")]
    [InlineData("wrongDatabase")]
    [InlineData("wrongRole")]
    [InlineData("missingPassword")]
    [InlineData("extraMigration")]
    [InlineData("missingModule")]
    [InlineData("moduleDestination")]
    [InlineData("mailboxInTesting")]
    public void Parse_InvalidProtocolOrDestination_RejectsWithoutSecretDisclosure(string fault)
    {
        var value = Snapshot();
        switch (fault)
        {
            case "environment": value["environment"] = "Development"; break;
            case "source": value["source"] = "Infisical"; break;
            case "oldSchema": value["schemaVersion"] = 3; break;
            case "missingSigningKey": value["secrets"]!.AsObject().Remove("IdentityAccess__Jwt__PrivateKeyPem"); break;
            case "missingAuthPolicy": value.Remove("authenticationPolicy"); break;
            case "schemaVersion": value["schemaVersion"] = 1; break;
            case "extraRootKey": value["unexpected"] = Marker; break;
            case "extraMigration": value["secrets"]!["ConnectionStrings__IdentityAccessMigrationDatabase"] = Marker; break;
            case "missingModule": value["secrets"]!.AsObject().Remove("ConnectionStrings__IdentityAccessDatabase"); break;
            case "moduleDestination": value["secrets"]!["ConnectionStrings__IdentityAccessDatabase"] = "Host=127.0.0.1;Port=5555;Database=owned_tests;Username=educenteros_identity_runtime;Password=" + Marker; break;
            case "mailboxInTesting": value["developmentMailboxDirectory"] = Marker; break;
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
        => AssertSafeRejection(Snapshot().ToJsonString().Replace("\"schemaVersion\":4", "\"schemaVersion\":4,\"SchemaVersion\":4", StringComparison.Ordinal));

    [Fact]
    public void Parse_OversizedSnapshot_Rejects() => AssertSafeRejection(new string('x', 65537));

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "EduCenterOS.sln"))) return directory.FullName;

        throw new InvalidOperationException("Test.RootMissing");
    }

    private static void AssertSafeRejection(string? json)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RuntimeSnapshot.Parse(json, "Testing"));
        Assert.StartsWith("Configuration.InvalidRuntimeSnapshot", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.False(exception.ToString().Contains(Marker, StringComparison.Ordinal));
    }
}
