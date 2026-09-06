using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.Application.Common.ViewModels;

/// <summary>
/// Metadatos de paginación para listas MVC.
/// </summary>
/// <remarks>
/// Los valores son resultado del servidor. La validación de solicitudes de
/// paginación pertenece a los validators de Application y usa los límites de
/// <see cref="PageRequest"/>.
/// </remarks>
public sealed class PaginationViewModel {
    /// <summary>Página actual, comenzando en uno.</summary>
    public int Page { get; init; } = PageRequest.DefaultPage;

    /// <summary>Cantidad de registros solicitada para la página.</summary>
    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    /// <summary>Cantidad total de registros que cumplen el filtro.</summary>
    public int TotalItems { get; init; }

    /// <summary>Cantidad total de páginas disponibles.</summary>
    public int TotalPages =>
        TotalItems <= 0 || PageSize <= 0 ? 0 : 1 + (TotalItems - 1) / PageSize;

    /// <summary>Indica si existe una página anterior.</summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>Indica si existe una página siguiente.</summary>
    public bool HasNextPage => Page < TotalPages;
}
