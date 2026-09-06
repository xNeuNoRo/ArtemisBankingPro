using System.ComponentModel.DataAnnotations;

namespace ArtemisBankingPro.Application.Features.Auth.ViewModels;

/// <summary>
/// MVC login form. It is mapped only to WebAppLoginCommand; it is never an API
/// response and never contains a JWT or session data.
/// </summary>
public sealed class LoginViewModel {
    [Required(ErrorMessage = "El nombre de usuario es requerido.")]
    [StringLength(50, ErrorMessage = "El nombre de usuario no puede exceder 50 caracteres.")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es requerida.")]
    [StringLength(128, ErrorMessage = "La contraseña no puede exceder 128 caracteres.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}
