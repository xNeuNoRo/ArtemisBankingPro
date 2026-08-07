namespace ArtemisBankingPro.Domain.Common.Pagination;

/// <summary>
/// Representa una solicitud de paginación.
/// Página 1 por defecto, tamaño máximo 20.
/// </summary>
public sealed class PageRequest {
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 20;

    public PageRequest(int? page = null, int? pageSize = null) {
        Page = page is null or < DefaultPage ? DefaultPage : page.Value;
        PageSize =
            pageSize is null or < 1 || pageSize.Value > MaxPageSize
                ? DefaultPageSize
                : pageSize.Value;
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;
}
