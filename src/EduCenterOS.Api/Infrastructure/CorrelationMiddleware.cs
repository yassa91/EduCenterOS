namespace EduCenterOS.Api.Infrastructure;

internal sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    internal const string ItemKey = "EduCenterOS.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-Id"] = correlationId;
            context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["correlationId"] = correlationId });

        await next(context);
    }
}
