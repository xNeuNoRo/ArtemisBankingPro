namespace ArtemisBankingPro.Application.Interfaces.Services;

/// <summary>
/// Motivo por el cual un nonce de confirmación no es válido. Los motivos de
/// contexto (actor, operación, huella) comparten un mensaje genérico para no
/// revelar información sensible sobre operaciones de otros actores.
/// </summary>
public enum ConfirmationTokenInvalidReason {
    NotFound = 1,
    AlreadyConsumed = 2,
    Expired = 3,
    ActorMismatch = 4,
    OperationMismatch = 5,
    FingerprintMismatch = 6,
}

/// <summary>
/// Resultado de validar y consumir un nonce de confirmación. Si no es válido,
/// <see cref="InvalidReason"/> indica el motivo y <see cref="ErrorMessage"/> un
/// mensaje seguro para el usuario.
/// </summary>
public sealed record ConfirmationValidationResult(
    bool IsValid,
    ConfirmationTokenInvalidReason? InvalidReason,
    string? ErrorMessage
) {
    public static ConfirmationValidationResult Valid() =>
        new(true, null, null);

    public static ConfirmationValidationResult Invalid(ConfirmationTokenInvalidReason reason) =>
        new(false, reason, DefaultMessage(reason));

    private static string DefaultMessage(ConfirmationTokenInvalidReason reason) =>
        reason switch {
            ConfirmationTokenInvalidReason.NotFound =>
                "El enlace de confirmación no es válido o ya no está disponible.",
            ConfirmationTokenInvalidReason.AlreadyConsumed =>
                "Esta confirmación ya fue utilizada.",
            ConfirmationTokenInvalidReason.Expired =>
                "La confirmación ha expirado. Realice la operación nuevamente.",
            _ =>
                "La confirmación no corresponde a esta operación.",
        };
}
