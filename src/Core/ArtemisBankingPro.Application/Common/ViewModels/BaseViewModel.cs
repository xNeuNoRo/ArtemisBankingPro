namespace ArtemisBankingPro.Application.Common.ViewModels;

/// <summary>
/// Contexto de presentación compartido por las pantallas MVC.
/// </summary>
/// <remarks>
/// Sus propiedades son contexto emitido por el servidor. No representan una
/// fuente confiable para comandos y no deben copiarse desde un POST al caso de
/// uso. Los formularios deben mapear únicamente sus campos editables.
/// </remarks>
public abstract class BaseViewModel {
    /// <summary>Texto que identifica la pantalla actual.</summary>
    public string PageTitle { get; init; } = string.Empty;

    /// <summary>Identificador del usuario autenticado para contexto visual.</summary>
    public string? CurrentUserId { get; init; }

    /// <summary>Nombre de usuario mostrado en la navegación.</summary>
    public string? CurrentUserName { get; init; }

    /// <summary>Rol efectivo de la sesión actual, ya resuelto por el servidor.</summary>
    public string? CurrentUserRole { get; init; }

    /// <summary>Indica si existe una sesión autenticada.</summary>
    public bool IsAuthenticated { get; init; }

    /// <summary>
    /// Clave estable del elemento de navegación activo; no es una URL recibida
    /// del cliente.
    /// </summary>
    public string? ActiveNavigationItem { get; init; }

    /// <summary>Mensajes seguros preparados por la capa de presentación.</summary>
    public IReadOnlyList<string> Messages { get; init; } = [];
}
