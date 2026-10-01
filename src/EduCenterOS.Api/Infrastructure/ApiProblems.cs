using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EduCenterOS.Api.Infrastructure;

internal static class ApiProblems
{
    internal static Task WriteStatusAsync(HttpContext context, int? statusOverride = null)
    {
        var status = statusOverride ?? context.Response.StatusCode;

        var (suffix, title, code, detail) = status switch
        {
            400 => ("validation", "Validation failed", "Validation.Failed", "The request is invalid."),
            404 => ("not-found", "Resource not found", "Http.RouteNotFound", "The requested route was not found."),
            405 => ("method-not-allowed", "Method not allowed", "Http.MethodNotAllowed", "The HTTP method is not supported for this route."),
            406 => ("not-acceptable", "Representation not acceptable", "Http.NotAcceptable", "The requested representation is not supported."),
            413 => ("payload-too-large", "Payload too large", "Http.PayloadTooLarge", "The request exceeds the permitted size."),
            415 => ("unsupported-media-type", "Unsupported media type", "Http.UnsupportedMediaType", "The request media type is not supported."),
            _ => ("unexpected", "Unexpected error", "General.UnexpectedError", "An unexpected error occurred.")
        };

        if (status is not (400 or 404 or 405 or 406 or 413 or 415))
            status = StatusCodes.Status500InternalServerError;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Type = $"urn:educenteros:problem:{suffix}",
            Title = title,
            Status = status,
            Detail = detail
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = context.Items[CorrelationMiddleware.ItemKey];
        if (Activity.Current is { } activity)
            problem.Extensions["traceId"] = activity.TraceId.ToHexString();

        return HttpMethods.IsHead(context.Request.Method)
            ? Task.CompletedTask
            : context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }
}
