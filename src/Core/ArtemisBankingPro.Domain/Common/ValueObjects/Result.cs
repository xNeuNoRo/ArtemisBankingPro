namespace ArtemisBankingPro.Domain.Common.ValueObjects;

/// <summary>
/// Contrato no genérico de resultado para que los behaviors de la capa
/// Application puedan inspeccionar éxito/fallo sin reflexión. Lo implementan
/// <see cref="Result"/> y <see cref="Result{T}"/>.
/// </summary>
public interface IResult {
    bool IsSuccess { get; }

    DomainError? Error { get; }

    /// <summary>
    /// Referencia de negocio del resultado exitoso (p. ej. <c>OperationId</c> o el
    /// identificador del producto afectado), para trazabilidad de idempotencia.
    /// </summary>
    string? ResultReference { get; }
}

/// <summary>
/// Representa el resultado de una operación, que puede ser exitosa o fallida, y puede contener un valor o un error de dominio.
/// </summary>
public sealed class Result : IResult {
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

    public string? ResultReference => null;

    public DomainError? Error { get; }

    public static Result Success() => new(true, null);

    public static Result<T> Success<T>(T value) => new Result<T>(true, value, null);

    public static Result Failure(DomainError error) =>
        new(false, error ?? throw new ArgumentNullException(nameof(error)));

    public static Result<T> Failure<T>(DomainError error) =>
        new Result<T>(false, default, error ?? throw new ArgumentNullException(nameof(error)));
}

public sealed class Result<T> : IResult {
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

    /// <summary>
    /// Referencia de negocio del resultado exitoso (p. ej. <c>OperationId</c>).
    /// No se serializa el DTO: la referencia debe ser explícita, estable y
    /// acotada; el behavior de idempotencia usa la clave del
    /// caller como referencia cuando no se provee una.
    /// </summary>
    public string? ResultReference => null;

    public DomainError? Error { get; }

    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("Un resultado fallido no tiene valor.");
}
