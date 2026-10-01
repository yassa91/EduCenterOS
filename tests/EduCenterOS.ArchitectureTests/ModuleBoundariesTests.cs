using System.Xml.Linq;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Xunit;
namespace EduCenterOS.ArchitectureTests;

public sealed class ModuleBoundariesTests
{
    [Fact]
    public void BuildingBlocks_HaveNoBusinessOrHttpDependencies()
    {
        Assert.DoesNotContain(typeof(IClock).Assembly.GetReferencedAssemblies(), value =>
            value.Name!.Contains("Modules", StringComparison.Ordinal) || value.Name.Contains("Api", StringComparison.Ordinal)
            || value.Name.Contains("AspNetCore", StringComparison.Ordinal) || value.Name.Contains("EntityFramework", StringComparison.Ordinal));
    }
    [Fact]
    public void IdentityPersistenceAndDomain_AreInternalAndDoNotDependOnHost()
    {
        var assembly = typeof(IdentityAccessDbContext).Assembly;
        Assert.DoesNotContain(assembly.GetExportedTypes(), type => type.Namespace?.Contains("Domain", StringComparison.Ordinal) == true
            || type.Namespace?.Contains("Persistence", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "EduCenterOS.Api");
        Assert.False(typeof(IdentityAccessDbContext).IsPublic);
    }
}
