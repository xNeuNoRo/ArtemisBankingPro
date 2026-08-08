namespace ArtemisBankingPro.Application.Common.Interfaces;

/// <summary>
/// Marca un Command cuya ejecución debe ser idempotente: repetir la misma
/// solicitud con la misma clave no aplica el efecto dos veces.
/// El <c>IdempotencyBehavior</c> reserva la clave antes de ejecutar y la
/// completa al terminar.
/// </summary>
public interface IIdempotentCommand {
    /// <summary>
    /// Clave única por actor que identifica la operación. Debe derivarse de
    /// datos estables del command (por ejemplo "create-loan-{clientId}-{monto}-{plazo}").
    /// </summary>
    string IdempotencyKey { get; }

    /// <summary>
    /// Huella del payload canónico: permite detectar la reutilización de la
    /// misma clave con un payload distinto.
    /// </summary>
    string RequestFingerprint { get; }
}
