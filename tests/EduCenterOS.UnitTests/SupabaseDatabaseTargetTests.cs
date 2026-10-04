using EduCenterOS.Modules.IdentityAccess.Contracts;
using Npgsql;
using Xunit;

namespace EduCenterOS.UnitTests;

public sealed class SupabaseDatabaseTargetTests
{
    private static SupabaseDatabaseTarget Target(string environment = "Testing", bool pooled = false) => new()
    {
        ProjectReference = "abcdefghijklmnopqrst",
        Environment = environment,
        ServerMajor = 17,
        Host = pooled ? "aws-0-eu-west-1.pooler.supabase.com" : "db.abcdefghijklmnopqrst.supabase.co"
    };

    private static string Connection(SupabaseDatabaseTarget target) => new NpgsqlConnectionStringBuilder
    {
        Host = target.Host,
        Database = "postgres",
        Port = 5432,
        SslMode = SslMode.VerifyFull,
        Username = target.Role("runtime") + (target.Host.StartsWith("aws-", StringComparison.Ordinal) ? "." + target.ProjectReference : ""),
        Password = "synthetic-test-marker"
    }.ConnectionString;

    [Theory]
    [InlineData("Testing", false)]
    [InlineData("Testing", true)]
    [InlineData("Development", false)]
    [InlineData("Development", true)]
    public void ReviewedDirectAndSessionTargets_AcceptVerifiedTlsAndRestrictedRole(string environment, bool pooled)
    {
        var target = Target(environment, pooled);
        Assert.Equal(SslMode.VerifyFull, target.Validate(Connection(target), environment, "runtime").SslMode);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("wrong-project")]
    [InlineData("transaction-pooler")]
    [InlineData("plaintext")]
    [InlineData("require-without-verification")]
    [InlineData("admin")]
    [InlineData("other-environment")]
    [InlineData("wrong-database")]
    [InlineData("include-error-detail")]
    [InlineData("log-parameters")]
    [InlineData("prepared-statements")]
    [InlineData("multiplexing")]
    [InlineData("options")]
    public void UnreviewedDestinationsAndTlsDowngrades_FailBeforeConnection(string fault)
    {
        var target = Target();
        var connection = new NpgsqlConnectionStringBuilder(Connection(target));
        switch (fault)
        {
            case "local": connection.Host = "127.0.0.1"; break;
            case "wrong-project": connection.Host = "db.zyxwvutsrqponmlkjihg.supabase.co"; break;
            case "transaction-pooler": connection.Port = 6543; break;
            case "plaintext": connection.SslMode = SslMode.Disable; break;
            case "require-without-verification": connection.SslMode = SslMode.Require; break;
            case "admin": connection.Username = "postgres"; break;
            case "other-environment": connection.Username = "educenteros_dev_runtime"; break;
            case "wrong-database": connection.Database = "educenteros_dev"; break;
            case "include-error-detail": connection.IncludeErrorDetail = true; break;
            case "log-parameters": connection.LogParameters = true; break;
            case "prepared-statements": connection.MaxAutoPrepare = 10; break;
            case "multiplexing": connection.Multiplexing = true; break;
            case "options": connection.Options = "-c role=postgres"; break;
        }
        var error = Assert.Throws<InvalidOperationException>(() => target.Validate(connection.ConnectionString, "Testing", "runtime"));
        Assert.Equal("Database.InvalidSupabaseTarget", error.Message);
        Assert.DoesNotContain("synthetic-test-marker", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentTarget_CannotBeReusedAsTesting()
        => Assert.Throws<InvalidOperationException>(() => Target("Development").Validate(Connection(Target("Development")), "Testing", "runtime"));
}
