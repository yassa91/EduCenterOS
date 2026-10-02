using System.Collections.ObjectModel;

namespace EduCenterOS.BuildingBlocks.Results;

public enum ErrorCategory
{
    Validation,
    Authentication,
    Authorization,
    NotFound,
    Conflict,
    BusinessRule,
    RateLimited,
    ServiceUnavailable
}

public sealed record ValidationIssue(
    string MemberPath,
    string Code,
    string Description
);

public sealed class Error
{
    public string Code { get; }
    public ErrorCategory Category { get; }
    public string Description { get; }
    public IReadOnlyList<ValidationIssue> ValidationIssues { get; }
    public bool ValidationIssuesTruncated { get; }
    public int? RetryDelaySeconds { get; }

    public Error(string code, ErrorCategory category, string description, int? retryDelaySeconds = null)
        : this(code, category, description, [], false, retryDelaySeconds) { }

    private Error(string code, ErrorCategory category, string description,
        IReadOnlyList<ValidationIssue> issues, bool truncated, int? retryDelaySeconds)
    {
        if (
            string.IsNullOrWhiteSpace(code) ||
            code.Length > 128 ||
            !Enum.IsDefined(category) ||
            string.IsNullOrWhiteSpace(description) ||
            description.Length > 500 ||
            retryDelaySeconds is <= 0 ||
            (retryDelaySeconds is not null && category is not (ErrorCategory.RateLimited or ErrorCategory.ServiceUnavailable))
        )
        {
            throw new ArgumentException("Result.InvalidErrorContract");
        }

        Code = code;
        Category = category;
        Description = description;
        ValidationIssues = issues;
        ValidationIssuesTruncated = truncated;
        RetryDelaySeconds = retryDelaySeconds;
    }

    public static Error Validation(IEnumerable<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var distinct = issues.DistinctBy(issue => (issue.MemberPath, issue.Code)).Take(51).ToArray();

        if (
            distinct.Length == 0 ||
            distinct.Any(
                issue => string.IsNullOrWhiteSpace(issue.MemberPath) ||
                issue.MemberPath.Length > 128 ||
                string.IsNullOrWhiteSpace(issue.Code) ||
                issue.Code.Length > 128 ||
                string.IsNullOrWhiteSpace(issue.Description) ||
                issue.Description.Length > 500
            )
        )
        {
            throw new ArgumentException("Result.InvalidValidationIssues");
        }

        var bounded = distinct.Take(50)
        .OrderBy(issue => issue.MemberPath, StringComparer.Ordinal)
        .ThenBy(issue => issue.Code, StringComparer.Ordinal).ToArray();

        return new Error(
            "Validation.Failed",
            ErrorCategory.Validation,
            "The request is invalid.",
            new ReadOnlyCollection<ValidationIssue>(bounded),
            distinct.Length > 50,
            null
        );
    }
}
