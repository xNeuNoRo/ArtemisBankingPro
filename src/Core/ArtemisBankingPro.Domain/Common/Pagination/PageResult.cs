namespace ArtemisBankingPro.Domain.Common.Pagination;

/// <summary>
/// Representa un resultado de paginación.
/// </summary>
public sealed record PageResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize) {
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;
}
