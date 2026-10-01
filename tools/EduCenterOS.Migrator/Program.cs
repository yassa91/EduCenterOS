using System.Text.Json;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 0) throw new InvalidOperationException();
            var raw = Environment.GetEnvironmentVariable("EDUCENTEROS_MIGRATION_SNAPSHOT");
            if (raw is null || raw.Length > 4096) throw new InvalidOperationException();
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var expected = new[] { "schemaVersion", "environment", "source", "connectionString" };
            var actual = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (actual.Length != expected.Length || actual.Except(expected, StringComparer.Ordinal).Any()
                || root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("environment").GetString() != "Development"
                || root.GetProperty("source").GetString() != "Infisical") throw new InvalidOperationException();
            await DevelopmentMigrations.ApplyAsync(root.GetProperty("connectionString").GetString()!);
            Console.WriteLine("IdentityAccess development migrations completed on the validated owned target."); return 0;
        }
        catch (Exception) { Console.Error.WriteLine("Migrations.Failed: explicit target, role and ownership validation are required."); return 1; }
    }
}
