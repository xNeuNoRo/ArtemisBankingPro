namespace ArtemisBankingPro.Application.Interfaces.Services;

/// <summary>
/// Emite y valida nonces de confirmación de una sola utilización (single-use)
/// para los flujos de confirmación de la WebApp.
///
/// El nonce es emitido por el servidor, vinculado al actor y a la operación
/// (tipo + huella del payload), expira y se consume una sola vez. La WebApp lo
/// utiliza como clave de idempotencia del command correspondiente; combinado
/// con <c>IdempotencyBehavior</c>, un doble clic, un refresh o un reintento no
/// puede duplicar una operación.
/// </summary>
public interface IConfirmationTokenService {
    /// <summary>
    /// Emite un nonce criptográficamente seguro y persiste solo su hash.
    /// Devuelve el nonce en claro, única vez.
    /// </summary>
    Task<string> IssueAsync(
        string actorId,
        string operationType,
        string requestFingerprint,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Valida el nonce (existe, del actor, de la operación, no consumido, no
    /// expirado) y, si es válido, lo consume atómicamente. Un nonce válido
    /// solo puede consumirse una vez.
    /// </summary>
    Task<ConfirmationValidationResult> ValidateAndConsumeAsync(
        string token,
        string actorId,
        string operationType,
        string requestFingerprint,
        CancellationToken cancellationToken = default
    );
}
