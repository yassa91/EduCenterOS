using EduCenterOS.Api.Middleware;
using EduCenterOS.Modules.IdentityAccess;
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
                // Swagger's parseFunction extracts parameters from parentheses in a function declaration.
                options.UseRequestInterceptor("function (request) { if (request.method === 'POST' && new URL(request.url, window.location.origin).pathname.startsWith('/api/v1/auth/')) { request.headers['X-EduCenterOS-Auth'] = '1'; } return request; }");
            });
        }

        app.UseRouting();
        app.UseIdentityAccessBrowserSecurity(ApiProblems.WriteErrorAsync);
        app.UseRateLimiter();
        app.UseIdentityAccessAuthentication(ApiProblems.WriteErrorAsync);
        app.UseStatusCodePages(context => ApiProblems.WriteStatusAsync(context.HttpContext));
    }
}
