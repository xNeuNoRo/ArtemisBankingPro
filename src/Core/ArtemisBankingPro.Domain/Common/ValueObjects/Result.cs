namespace ArtemisBankingPro.Domain.Common.ValueObjects;

/// <summary>
/// Representa el resultado de una operación, que puede ser exitosa o fallida, y puede contener un valor o un error de dominio.
/// </summary>
public sealed class Result {
    private Result(bool isSuccess, DomainError? error) {
        if (isSuccess == (error is not null)) {
            throw new ArgumentException(
                "Un resultado debe contener éxito o un error, pero no ambos."
            );
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError? Error { get; }

    public static Result Success() => new(true, null);

    public static Result<T> Success<T>(T value) => new Result<T>(true, value, null);

    public static Result Failure(DomainError error) =>
        new(false, error ?? throw new ArgumentNullException(nameof(error)));

    public static Result<T> Failure<T>(DomainError error) =>
        new Result<T>(false, default, error ?? throw new ArgumentNullException(nameof(error)));
}

public sealed class Result<T> {
    private readonly T? _value;

    internal Result(bool isSuccess, T? value, DomainError? error) {
        if (isSuccess == (error is not null)) {
            throw new ArgumentException(
                "Un resultado debe contener éxito o un error, pero no ambos."
            );
        }

        if (isSuccess && value is null) {
            throw new ArgumentNullException(
                nameof(value),
                "Un resultado exitoso requiere un valor."
            );
        }

        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError? Error { get; }

    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("Un resultado fallido no tiene valor.");
}
