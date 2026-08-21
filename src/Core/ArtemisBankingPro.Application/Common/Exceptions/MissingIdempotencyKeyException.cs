namespace ArtemisBankingPro.Application.Common.Exceptions;

/// <summary>
/// Indicates that a caller omitted the key required by an idempotent command.
/// The API boundary maps it to the stable Idempotency.MissingKey contract.
/// </summary>
public sealed class MissingIdempotencyKeyException()
    : Exception("La clave de idempotencia es obligatoria.");
