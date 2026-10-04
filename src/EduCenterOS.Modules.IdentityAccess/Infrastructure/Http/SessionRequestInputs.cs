using System.Globalization;
using EduCenterOS.BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;

internal readonly record struct SessionPage(int Page, int PageSize);

internal static class SessionRequestInputs
{
    internal static Result<SessionPage> Page(HttpContext context)
    {
        var query = context.Request.Query;

        if (query.Keys.Any(name => name is not ("page" or "pageSize")))
            return Result<SessionPage>.Failure(new Error(
                "Validation.Failed",
                ErrorCategory.Validation,
                "The request is invalid."
            ));

        var issues = new List<ValidationIssue>();
        var page = Integer(query["page"], 1);
        var size = Integer(query["pageSize"], 20);

        if (page is null) issues.Add(Issue("page"));
        if (size is null || size > 100) issues.Add(Issue("pageSize"));

        return issues.Count == 0
            ? Result<SessionPage>.Success(new SessionPage(page!.Value, size!.Value))
            : Result<SessionPage>.Failure(Error.Validation(issues));
    }

    internal static Result<Guid> SessionId(HttpContext context)
    {
        var text = context.Request.RouteValues["sessionId"]?.ToString();

        return text is { Length: 36 } &&
            Guid.TryParseExact(text, "D", out var id) &&
            id != Guid.Empty &&
            id.ToString("D") == text
            ? Result<Guid>.Success(id)
            : Result<Guid>.Failure(Error.Validation([Issue("sessionId")]));
    }

    private static int? Integer(StringValues values, int fallback)
    {
        if (values.Count == 0) return fallback;

        return values.Count == 1 &&
            values[0] is { Length: > 0 and <= 10 } text &&
            text.All(char.IsAsciiDigit) &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
            value > 0 ? value : null;
    }

    private static ValidationIssue Issue(string member) => new(member, "IdentityAccess.Input.Invalid", "The field is invalid.");
}
