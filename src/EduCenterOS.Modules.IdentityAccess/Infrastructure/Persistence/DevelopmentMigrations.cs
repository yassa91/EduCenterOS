using EduCenterOS.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;

internal static class DevelopmentMigrations
{
    internal static async Task ApplyAsync(string connectionString, SupabaseDatabaseTarget databaseTarget)
    {
        var target = databaseTarget.Validate(connectionString, "Development", "migration");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT current_database(), current_user, current_setting('server_version_num')::integer,
                (SELECT r.rolname FROM pg_namespace n JOIN pg_roles r ON r.oid=n.nspowner WHERE n.nspname='identity_access'),
                (SELECT rolsuper FROM pg_roles WHERE rolname=current_user)
            """,
            connection
        );

        await using (var reader = await command.ExecuteReaderAsync())
            if (
                !await reader.ReadAsync() ||
                reader.GetString(0) != target.Database ||
                reader.GetString(1) != databaseTarget.Role("migration") ||
                reader.GetInt32(2) / 10000 != databaseTarget.ServerMajor ||
                reader.IsDBNull(3) ||
                reader.GetString(3) != databaseTarget.Role("migration") ||
                reader.GetBoolean(4)
            )
                throw new InvalidOperationException("Migrations.OwnershipMismatch");

        await using var context = new IdentityAccessDbContext(IdentityAccessDbContext.Options(connectionString));
        await context.Database.MigrateAsync();
    }
}
