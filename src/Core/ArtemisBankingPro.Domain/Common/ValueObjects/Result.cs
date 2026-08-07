namespace ArtemisBankingPro.Domain.Common.ValueObjects;

public sealed class Result {
    private Result(bool isSuccess, DomainError? error) {
        if (isSuccess == (error is not null)) {
            throw new ArgumentException("A result must contain either success or an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(DomainError error) =>
        new(false, error ?? throw new ArgumentNullException(nameof(error)));

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(DomainError error) => Result<T>.Failure(error);
}

public sealed class Result<T> {
    private readonly T? _value;

    private Result(bool isSuccess, T? value, DomainError? error) {
        if (isSuccess == (error is not null)) {
            throw new ArgumentException("A result must contain either success or an error.");
        }

        if (isSuccess && value is null) {
            throw new ArgumentNullException(nameof(value), "A successful result requires a value.");
        }

        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError? Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(DomainError error) =>
        new(false, default, error ?? throw new ArgumentNullException(nameof(error)));

}
