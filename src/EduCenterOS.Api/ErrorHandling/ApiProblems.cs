using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Api.Middleware;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EduCenterOS.Api.ErrorHandling;

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

        return WriteAsync(context, status, suffix, title, code, detail, null);
    }

    internal static IResult FromStatus(int status) => new StatusResponse(status);

    internal static IResult FromError(Error error) => new ErrorResponse(error);

    internal static Task WriteErrorAsync(HttpContext context, Error error)
    {
        var registered = error.Code switch
        {
            "Validation.Failed" => error.Category == ErrorCategory.Validation,
            "IdentityAccess.VerificationRejected" => error.Category == ErrorCategory.BusinessRule,
            "IdentityAccess.RegistrationRejected" => error.Category == ErrorCategory.Conflict,
            "Infrastructure.RateLimitExceeded" => error.Category == ErrorCategory.RateLimited,
            "Infrastructure.DeliveryUnavailable" or "Infrastructure.Busy" or "Infrastructure.Timeout" => error.Category == ErrorCategory.ServiceUnavailable,
            _ => false
        };
        if (!registered) return WriteStatusAsync(context, 500);
        var (status, suffix, title) = error.Category switch
        {
            ErrorCategory.Validation => (400, "validation", "Validation failed"),
            ErrorCategory.Conflict => (409, "conflict", "Request conflict"),
            ErrorCategory.BusinessRule => (422, "business-rule", "Business rule rejected"),
            ErrorCategory.RateLimited => (429, "rate-limited", "Rate limit exceeded"),
            ErrorCategory.ServiceUnavailable => (503, "service-unavailable", "Service unavailable"),
            _ => (500, "unexpected", "Unexpected error")
        };
        return WriteAsync(context, status, suffix, title, error.Code, error.Description, error);
    }

    private static Task WriteAsync(HttpContext context, int status, string suffix, string title, string code, string detail, Error? error)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Type = $"urn:educenteros:problem:{suffix}",
            Title = title,
            Status = status,
            Detail = detail
        };
        if (error?.RetryDelaySeconds is { } delay) context.Response.Headers.RetryAfter = delay.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (error?.ValidationIssues.Count > 0)
        {
            var members = new[] { "phoneNumber", "code", "verificationProof", "challengeId", "fullName", "password", "emailAddress" };
            if (error.ValidationIssues.Any(issue => !members.Contains(issue.MemberPath, StringComparer.Ordinal) || issue.Code != "IdentityAccess.Input.Invalid"))
                return WriteStatusAsync(context, 500);
            problem.Extensions["errors"] = error.ValidationIssues.GroupBy(issue => "body." + issue.MemberPath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(issue => new { code = issue.Code, description = issue.Description }).ToArray(), StringComparer.Ordinal);
            problem.Extensions["errorsTruncated"] = error.ValidationIssuesTruncated;
        }
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = context.Items[CorrelationMiddleware.ItemKey];
        if (Activity.Current is { } activity)
            problem.Extensions["traceId"] = activity.TraceId.ToHexString();

        return HttpMethods.IsHead(context.Request.Method)
            ? Task.CompletedTask
            : context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }

    private sealed class StatusResponse(int status) : IResult
    {
        public Task ExecuteAsync(HttpContext context) => WriteStatusAsync(context, status);
    }

    private sealed class ErrorResponse(Error error) : IResult
    {
        public Task ExecuteAsync(HttpContext context) => WriteErrorAsync(context, error);
    }
}
