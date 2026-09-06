namespace ArtemisBankingPro.Application.Features.Merchants.DTOs;

/// <summary>
/// Respuesta paginada del listado de comercios (spec §40, GET /api/commerce).
/// </summary>
public sealed record GetMerchantsPagedResponseDto(
    int Page,
    int PageSize,
    int TotalRecords,
    int TotalPages,
    IReadOnlyList<MerchantSummaryDto> Data
);
