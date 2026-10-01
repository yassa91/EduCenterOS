using Microsoft.EntityFrameworkCore.Design;
namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;

// Generation only. No developer/runtime connection or secret fallback is available to tooling.
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<IdentityAccessDbContext>
{
    public IdentityAccessDbContext CreateDbContext(string[] args)
    {
        if (args.Length != 0 || Environment.GetEnvironmentVariable("EDUCENTEROS_MIGRATION_DESIGN") != "1")
            throw new InvalidOperationException("Migrations.ExplicitDesignModeRequired");
        return new IdentityAccessDbContext(IdentityAccessDbContext.Options(
            "Host=127.0.0.1;Port=1;Database=educenteros_design_only;Username=design_only;Password=not-a-runtime-credential;Timeout=1"));
    }
}
