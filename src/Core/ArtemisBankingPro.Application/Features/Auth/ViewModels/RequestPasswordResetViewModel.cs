using System.ComponentModel.DataAnnotations;

namespace ArtemisBankingPro.Application.Features.Auth.ViewModels;

/// <summary>
/// MVC form for requesting a password reset.
/// </summary>
public sealed class RequestPasswordResetViewModel {
    [Required(ErrorMessage = "El nombre de usuario es requerido.")]
    [StringLength(50, ErrorMessage = "El nombre de usuario no puede exceder 50 caracteres.")]
    public string UserName { get; set; } = string.Empty;
}
