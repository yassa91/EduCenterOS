using System.Xml.Linq;
using Xunit;

namespace EduCenterOS.ArchitectureTests;

public sealed class HostBoundariesTests
{
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "EduCenterOS.sln"))) return directory.FullName;
        throw new InvalidOperationException("Architecture.ProjectRootMissing");
    }

    [Fact]
    public void ProductionProjects_DoNotDependOnTestProjectsOrPackages()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Root(), "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var project = XDocument.Load(file);
            foreach (var reference in project.Descendants("ProjectReference"))
                Assert.False(((string?)reference.Attribute("Include") ?? "").Contains("tests", StringComparison.OrdinalIgnoreCase));
            foreach (var package in project.Descendants("PackageReference"))
            {
                var name = (string?)package.Attribute("Include") ?? "";
                Assert.False(name.Contains("xunit", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Testcontainers", StringComparison.OrdinalIgnoreCase)
                    || name.Contains(".Testing", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Test.Sdk", StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void TestProjects_DoNotReferenceEachOther_AndUseVSTest()
    {
        var files = Directory.GetFiles(Path.Combine(Root(), "tests"), "*.csproj", SearchOption.AllDirectories);
        Assert.Equal(3, files.Length);
        foreach (var file in files)
        {
            var project = XDocument.Load(file);
            Assert.Equal("false", project.Descendants("TestingPlatformDotnetTestSupport").Single().Value);
            Assert.Contains(project.Descendants("PackageReference"), item => (string?)item.Attribute("Include") == "xunit.runner.visualstudio");
            foreach (var reference in project.Descendants("ProjectReference"))
            {
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, (string)reference.Attribute("Include")!));
                Assert.StartsWith(Path.Combine(Root(), "src") + Path.DirectorySeparatorChar, target);
            }
        }
    }

    [Fact]
    public void HostInfrastructure_IsInternal_AndDoesNotExportBusinessSurface()
    {
        var exported = typeof(Program).Assembly.GetExportedTypes();
        Assert.Collection(exported, type => Assert.Equal(typeof(Program), type));
        Assert.DoesNotContain(typeof(Program).Assembly.GetReferencedAssemblies(), name =>
            name.Name?.Contains("Tests", StringComparison.OrdinalIgnoreCase) == true
            || name.Name?.Contains("Testcontainers", StringComparison.OrdinalIgnoreCase) == true);
    }
}
