namespace ArtemisBankingPro.Application.Common.ViewModels;

/// <summary>
/// Modelo seguro para presentar un error ya normalizado en MVC.
/// </summary>
public sealed class ErrorViewModel : BaseViewModel {
    /// <summary>Identificador de trazabilidad que puede mostrarse al usuario.</summary>
    public string? RequestId { get; init; }

    /// <summary>Código HTTP asociado al estado visual, si está disponible.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Título amigable del error.</summary>
    public string Title { get; init; } = "Error del sistema";

    /// <summary>Mensaje seguro para el usuario; nunca contiene excepciones internas.</summary>
    public string Message { get; init; } =
        "Ha ocurrido un error inesperado al procesar su solicitud.";

    /// <summary>Indica si el identificador de trazabilidad puede mostrarse.</summary>
    public bool ShowRequestId => !string.IsNullOrWhiteSpace(RequestId);
}
