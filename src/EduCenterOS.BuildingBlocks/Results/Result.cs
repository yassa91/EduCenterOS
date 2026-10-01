namespace EduCenterOS.BuildingBlocks.Results;

public class Result
{
    private readonly Error? error;
    public bool IsSuccess { get; }
    public Error Error => !IsSuccess ? error! : throw new InvalidOperationException("Result.SuccessHasNoError");
    protected Result(bool success, Error? error)
    {
        if (success == (error is not null)) throw new ArgumentException("Result.InconsistentState");
        IsSuccess = success; this.error = error;
    }
    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error ?? throw new ArgumentNullException(nameof(error)));
}

public sealed class Result<T> : Result
{
    private readonly T? value;
    public T Value => IsSuccess ? value! : throw new InvalidOperationException("Result.FailureHasNoValue");
    private Result(bool success, T? value, Error? error) : base(success, error) => this.value = value;
    public static Result<T> Success(T value) => new(true, value ?? throw new ArgumentNullException(nameof(value)), null);
    public new static Result<T> Failure(Error error) => new(false, default, error ?? throw new ArgumentNullException(nameof(error)));
}
