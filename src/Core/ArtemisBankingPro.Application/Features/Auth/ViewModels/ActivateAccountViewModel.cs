using System.ComponentModel.DataAnnotations;

namespace ArtemisBankingPro.Application.Features.Auth.ViewModels;

/// <summary>
/// MVC activation flow model. The token is transport-only input and is never
/// returned by a response ViewModel.
/// </summary>
public sealed class ActivateAccountViewModel {
    [Required(ErrorMessage = "El token es requerido.")]
    // MVC carries a time-limited Data Protection envelope; the Application
    // command validator enforces the raw token limit after unwrapping.
    [StringLength(1024, ErrorMessage = "El enlace de activación no es valido.")]
    public string Token { get; set; } = string.Empty;
}
