using EduCenterOS.Api.Middleware;
using EduCenterOS.Api.ErrorHandling;

namespace EduCenterOS.Api.Configuration;

internal static class ApiPipeline
{
    internal static void UseApiPipeline(this WebApplication app)
    {
        app.UseMiddleware<CorrelationMiddleware>();
        app.UseMiddleware<ErrorBoundaryMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "EduCenterOS API v1");
                options.DocumentTitle = "EduCenterOS API";
            });
        }

        app.UseRouting();
        app.UseRateLimiter();
        app.UseStatusCodePages(context => ApiProblems.WriteStatusAsync(context.HttpContext));
    }
}
