using EduCenterOS.Api.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace EduCenterOS.Api.Configuration;

internal static class ApiEndpointRegistration
{
    internal static void MapApiEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthResponse.WriteAsync
        }).WithMetadata(new HttpMethodMetadata(["GET", "HEAD"])).ExcludeFromDescription();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = HealthResponse.WriteAsync
        }).WithMetadata(new HttpMethodMetadata(["GET", "HEAD"])).ExcludeFromDescription();
    }
}
