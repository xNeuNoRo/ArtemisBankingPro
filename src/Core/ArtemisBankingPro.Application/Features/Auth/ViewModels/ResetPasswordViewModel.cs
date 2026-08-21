using System.ComponentModel.DataAnnotations;

namespace ArtemisBankingPro.Application.Features.Auth.ViewModels;

/// <summary>
/// MVC password-reset flow model. UserId and Token are untrusted route/form
/// values and are revalidated by the Application handler as a pair.
/// </summary>
public sealed class ResetPasswordViewModel {
    [Required(ErrorMessage = "El identificador del usuario es requerido.")]
    [StringLength(450, ErrorMessage = "El identificador del usuario no es válido.")]
    public string UserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "El token es requerido.")]
    // The MVC form carries a time-limited Data Protection envelope. The
    // Application command validator still enforces the raw token limit after
    // the WebApp unwraps it.
    [StringLength(1024, ErrorMessage = "El enlace de restablecimiento no es válido.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es requerida.")]
    [StringLength(
        128,
        MinimumLength = 8,
        ErrorMessage = "La contraseña debe tener entre 8 y 128 caracteres."
    )]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "La confirmación de la contraseña es requerida.")]
    [Compare(
        nameof(Password),
        ErrorMessage = "La contraseña y la confirmación de contraseña deben coincidir."
    )]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}
