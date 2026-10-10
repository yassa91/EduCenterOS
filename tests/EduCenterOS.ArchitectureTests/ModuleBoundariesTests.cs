using System.Reflection;
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

    [Fact]
    public void FeatureSignaturesAndFields_DoNotDependOnSiblingFeatures()
    {
        foreach (var type in typeof(IdentityAccessDbContext).Assembly.GetTypes())
        {
            var feature = Feature(type);

            if (feature is null or "Shared") continue;

            foreach (var dependency in SignatureDependencies(type).SelectMany(Expand))
            {
                var dependencyFeature = Feature(dependency);
                Assert.True(dependencyFeature is null or "Shared" || dependencyFeature == feature,
                    $"{type.FullName} depends on sibling feature {dependency.FullName}.");
            }
        }
    }

    [Fact]
    public void DomainSignaturesAndFields_DoNotDependOnInfrastructureOrHttp()
    {
        foreach (var type in typeof(IdentityAccessDbContext).Assembly.GetTypes().Where(type => type.Namespace?.EndsWith(".Domain", StringComparison.Ordinal) == true))
        {
            Assert.DoesNotContain(SignatureDependencies(type).SelectMany(Expand), dependency =>
                dependency.Namespace?.Contains(".Infrastructure", StringComparison.Ordinal) == true ||
                dependency.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true ||
                dependency.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true);
        }
    }

    private static string? Feature(Type type)
    {
        const string prefix = "EduCenterOS.Modules.IdentityAccess.Features.";

        return type.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true
            ? type.Namespace[prefix.Length..].Split('.')[0]
            : null;
    }

    private static IEnumerable<Type> SignatureDependencies(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags)) yield return field.FieldType;

        foreach (var method in type.GetMethods(flags))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters()) yield return parameter.ParameterType;
        }

        foreach (var constructor in type.GetConstructors(flags))
            foreach (var parameter in constructor.GetParameters()) yield return parameter.ParameterType;
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;

        if (type.HasElementType)
            foreach (var element in Expand(type.GetElementType()!)) yield return element;

        foreach (var argument in type.GetGenericArguments())
            foreach (var nested in Expand(argument)) yield return nested;
    }
}
