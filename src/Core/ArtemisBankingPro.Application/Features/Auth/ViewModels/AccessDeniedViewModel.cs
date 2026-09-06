using ArtemisBankingPro.Application.Common.ViewModels;

namespace ArtemisBankingPro.Application.Features.Auth.ViewModels;

/// <summary>
/// Safe output for an authenticated user who lacks permission for a section.
/// The home destination is a server-resolved navigation key, not a redirect URL.
/// </summary>
public sealed class AccessDeniedViewModel : BaseViewModel {
    public string Message { get; init; } = "No posee permisos para acceder a esta sección.";

    public string HomeNavigationKey { get; init; } = string.Empty;
}
