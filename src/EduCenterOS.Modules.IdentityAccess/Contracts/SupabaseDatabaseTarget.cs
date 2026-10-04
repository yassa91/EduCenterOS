using System.Text.RegularExpressions;
using Npgsql;

namespace EduCenterOS.Modules.IdentityAccess.Contracts;

// Non-secret target supplied by the trusted bootstrap, never by an HTTP request.
public sealed class SupabaseDatabaseTarget
{
    public required string ProjectReference { get; init; }
    public required string Host { get; init; }
    public required string Environment { get; init; }
    public required int ServerMajor { get; init; }

    public string Role(string purpose)
    {
        if (
            Environment is not ("Development" or "Testing") ||
            purpose is not ("probe" or "runtime" or "migration" or "owner")
        )
            throw new InvalidOperationException("Database.InvalidRolePurpose");

        return $"educenteros_{(Environment == "Development" ? "dev" : "test")}_{purpose}";
    }

    public NpgsqlConnectionStringBuilder Validate(string connectionString, string environment, string purpose)
    {
        var direct = Host == $"db.{ProjectReference}.supabase.co";
        var session = Regex.IsMatch(Host, @"^aws-[0-9]+-[a-z0-9-]+\.pooler\.supabase\.com\z", RegexOptions.CultureInvariant);
        var connection = new NpgsqlConnectionStringBuilder(connectionString);

        if (
            !Regex.IsMatch(ProjectReference, @"^[a-z0-9]{20}\z", RegexOptions.CultureInvariant) ||
            Environment != environment ||
            ServerMajor is not (17 or 18) ||
            !(direct || session) ||
            connection.Host != Host ||
            connection.Port != 5432 ||
            connection.Database != "postgres" ||
            connection.Username != (direct ? Role(purpose) : $"{Role(purpose)}.{ProjectReference}") ||
            string.IsNullOrEmpty(connection.Password) ||
            connection.SslMode != SslMode.VerifyFull ||
            connection.IncludeErrorDetail ||
            connection.LogParameters ||
            connection.Multiplexing ||
            connection.MaxAutoPrepare != 0 ||
            !string.IsNullOrEmpty(connection.Options)
        )
            throw new InvalidOperationException("Database.InvalidSupabaseTarget");

        return connection;
    }
}
