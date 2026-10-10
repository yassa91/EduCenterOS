using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenterOS.Modules.IdentityAccess;

public static partial class ModuleRegistration
{
    private static void AddPersistence(IServiceCollection services)
    {
        services.AddDbContextFactory<IdentityAccessDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IdentityAccessRuntimeSettings>().ConnectionString,
                postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "identity_access").CommandTimeout(5))
            .AddInterceptors(provider.GetServices<IInterceptor>()));
    }
}
