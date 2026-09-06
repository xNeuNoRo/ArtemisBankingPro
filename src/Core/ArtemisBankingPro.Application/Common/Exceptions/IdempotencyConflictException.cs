namespace ArtemisBankingPro.Application.Common.Exceptions;

/// <summary>
/// Se lanza cuando una key de idempotencia ya está reservada y en proceso,
/// o cuando la misma clave se reutiliza con un payload distinto.
/// Se traduce a 409 Conflict en la capa de presentación.
/// </summary>
public sealed class IdempotencyConflictException(string message) : Exception(message) {
    /// <summary>
    /// Referencia del resultado terminal original (OperationId para operaciones
    /// aprobadas; código de rechazo estable para rechazos persistidos). Permite
    /// a la capa de presentación reproducir el resultado original de un replay.
    /// </summary>
    public string? ResultReference { get; }

    public IdempotencyConflictException(string message, string? resultReference)
        : this(message) {
        ResultReference = resultReference;
    }
}
