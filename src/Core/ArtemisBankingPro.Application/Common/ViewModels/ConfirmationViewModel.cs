using System.ComponentModel.DataAnnotations;

namespace ArtemisBankingPro.Application.Common.ViewModels;

/// <summary>
/// Modelo de pantalla para una confirmación emitida por el servidor.
/// </summary>
/// <remarks>
/// El token es single-use, tiene expiración y está vinculado al actor y a la
/// operación. No se registra en logs. El handler debe revalidar estado,
/// ownership, fingerprint e idempotencia; los textos y valores mostrados no
/// autorizan ni calculan una operación financiera.
/// </remarks>
public class ConfirmationViewModel : BaseViewModel {
    /// <summary>
    /// Nonce de confirmación emitido por el servidor y devuelto por el POST.
    /// </summary>
    [Required(ErrorMessage = "El token de confirmación es requerido.")]
    [StringLength(256, ErrorMessage = "El token de confirmación no es válido.")]
    public string ConfirmationToken { get; set; } = string.Empty;

    /// <summary>Título de la pantalla de confirmación.</summary>
    public string Title { get; init; } = "Confirmar operación";

    /// <summary>Explicación segura de la acción que se solicita confirmar.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Texto del botón que confirma.</summary>
    public string ConfirmButtonText { get; init; } = "Confirmar";

    /// <summary>Texto del botón que cancela.</summary>
    public string CancelButtonText { get; init; } = "Cancelar";
}
