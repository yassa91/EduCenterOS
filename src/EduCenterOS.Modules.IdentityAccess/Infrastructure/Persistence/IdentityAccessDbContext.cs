using System.Text.RegularExpressions;
using EduCenterOS.Modules.IdentityAccess.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;

internal sealed class IdentityAccessDbContext(DbContextOptions<IdentityAccessDbContext> options) : DbContext(options)
{
    internal DbSet<UserSession> Sessions => Set<UserSession>();
    internal DbSet<RefreshTokenRecord> RefreshTokens => Set<RefreshTokenRecord>();
    internal DbSet<LoginTarget> LoginTargets => Set<LoginTarget>();
    internal DbSet<PersonIdentity> People => Set<PersonIdentity>();
    internal DbSet<UserAccount> Accounts => Set<UserAccount>();
    internal DbSet<OtpChallenge> Challenges => Set<OtpChallenge>();
    internal DbSet<VerificationTarget> Targets => Set<VerificationTarget>();

    internal static DbContextOptions<IdentityAccessDbContext> Options(string connectionString) => new DbContextOptionsBuilder<IdentityAccessDbContext>()
        .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "identity_access").CommandTimeout(5))
        .Options;

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("identity_access");
        model.ApplyConfigurationsFromAssembly(typeof(IdentityAccessDbContext).Assembly);

        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(Regex.Replace(property.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
            }
        }
    }
}
