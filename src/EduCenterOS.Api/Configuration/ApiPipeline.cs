using EduCenterOS.Api.Middleware;
using EduCenterOS.Api.ErrorHandling;

namespace EduCenterOS.Api.Configuration;

internal static class ApiPipeline
{
    internal static void UseApiPipeline(this WebApplication app)
    {
        app.UseMiddleware<CorrelationMiddleware>();
        app.UseMiddleware<ErrorBoundaryMiddleware>();
        app.UseStatusCodePages(context => ApiProblems.WriteStatusAsync(context.HttpContext));
    }
}
