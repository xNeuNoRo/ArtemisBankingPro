namespace ArtemisBankingPro.Application.Common.Exceptions;

/// <summary>
/// Se lanza cuando una key de idempotencia ya está reservada y en proceso,
/// o cuando la misma clave se reutiliza con un payload distinto.
/// Se traduce a 409 Conflict en la capa de presentación.
/// </summary>
public sealed class IdempotencyConflictException(string message) : Exception(message);
