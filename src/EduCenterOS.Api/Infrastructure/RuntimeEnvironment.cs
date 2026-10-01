namespace EduCenterOS.Api.Infrastructure;

internal static class RuntimeEnvironment
{
    internal static string Resolve()
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var aspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (dotnet is not null && aspnet is not null && dotnet != aspnet)
            throw new InvalidOperationException("Environment.Conflict: runtime environment selectors must agree.");

        var selected = dotnet ?? aspnet;
        if (selected is not ("Development" or "Testing" or "Staging" or "Production"))
            throw new InvalidOperationException("Environment.Invalid: an explicit canonical environment is required.");

        if (selected is "Staging" or "Production")
            throw new InvalidOperationException("Environment.NotEnabled: deployment requires the T39 runtime contract.");

        return selected;
    }
}
