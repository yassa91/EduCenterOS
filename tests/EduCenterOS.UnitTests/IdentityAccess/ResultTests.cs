using EduCenterOS.BuildingBlocks.Results;
using Xunit;
namespace EduCenterOS.UnitTests.IdentityAccess;

public sealed class ResultTests
{
    [Fact]
    public void ResultAccess_RejectsContradictoryState()
    {
        var success = Result<int>.Success(42); Assert.Equal(42, success.Value);
        Assert.Throws<InvalidOperationException>(() => success.Error);
        var error = new Error("Example.Rejected", ErrorCategory.Conflict, "The operation was rejected.");
        var failure = Result<int>.Failure(error); Assert.Same(error, failure.Error);
        Assert.Throws<InvalidOperationException>(() => failure.Value);
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
        Assert.Throws<ArgumentException>(() => new Error("", ErrorCategory.Conflict, "safe"));
        Assert.Throws<ArgumentException>(() => new Error("Example.Rejected", (ErrorCategory)99, "safe"));
        Assert.Throws<ArgumentException>(() => new Error("Example.Rejected", ErrorCategory.Conflict, "safe", 1));
    }
    [Fact]
    public void ValidationAggregation_IsBoundedSortedAndImmutable()
    {
        var input = Enumerable.Range(0, 70).Select(i => new ValidationIssue($"field{i:D2}", "Input.Invalid", "Invalid field.")).ToList();
        var error = Error.Validation(input); input.Clear();
        Assert.Equal(50, error.ValidationIssues.Count); Assert.True(error.ValidationIssuesTruncated);
        Assert.Equal("field00", error.ValidationIssues[0].MemberPath);
        Assert.Throws<NotSupportedException>(() => ((IList<ValidationIssue>)error.ValidationIssues).Clear());
        Assert.Throws<ArgumentException>(() => Error.Validation([]));
        var distinct = Error.Validation([new("name", "Input.Invalid", "Invalid field."), new("name", "Input.Invalid", "Invalid field.")]);
        Assert.Single(distinct.ValidationIssues); Assert.False(distinct.ValidationIssuesTruncated);
    }
}
