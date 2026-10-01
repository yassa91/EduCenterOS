using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EduCenterOS.Api.Health;

internal static class HealthResponse
{
    internal static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var healthy = report.Status == HealthStatus.Healthy;
        context.Response.StatusCode = healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/plain";
        return HttpMethods.IsHead(context.Request.Method)
            ? Task.CompletedTask
            : context.Response.WriteAsync(healthy ? "Healthy" : "Unhealthy", context.RequestAborted);
    }
}
