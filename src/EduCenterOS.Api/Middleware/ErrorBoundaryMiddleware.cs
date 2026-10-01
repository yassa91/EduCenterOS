using EduCenterOS.Api.ErrorHandling;
namespace EduCenterOS.Api.Middleware;

internal sealed class ErrorBoundaryMiddleware(RequestDelegate next, ILogger<ErrorBoundaryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(new EventId(1001, "UnexpectedFailure"),
                "Unhandled request failure ({ExceptionType}).", exception.GetType().Name);
            if (context.Response.HasStarted)
                throw;

            context.Response.Clear();
            await ApiProblems.WriteStatusAsync(context, StatusCodes.Status500InternalServerError);
        }
    }
}
