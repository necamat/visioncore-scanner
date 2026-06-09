namespace VisionCore.Application.Common;

/// <summary>
/// Outcome of an operation that either succeeds or fails with a human-readable
/// error message. Used at use-case and infrastructure boundaries where there is
/// no payload to return.
/// </summary>
public sealed record Result
{
    private Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public string? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(string error) => new(false, error);
}

/// <summary>
/// Outcome of an operation that yields a <typeparamref name="T"/> value on
/// success or a human-readable error on failure.
/// </summary>
public sealed record Result<T>
{
    private Result(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T? Value { get; }

    public string? Error { get; }

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(string error) => new(false, default, error);
}
