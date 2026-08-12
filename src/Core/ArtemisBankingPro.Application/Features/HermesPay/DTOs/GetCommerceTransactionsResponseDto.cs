namespace ArtemisBankingPro.Application.Features.HermesPay.DTOs;

/// <summary>
/// Respuesta paginada de las transacciones de un comercio (spec §41,
/// GET /pay/get-transactions). Campos exactos de la respuesta 200:
/// page, pageSize, totalRecords, totalPages, commerceId, commerceName y data.
/// </summary>
public sealed record GetCommerceTransactionsResponseDto(
    int Page,
    int PageSize,
    int TotalRecords,
    int TotalPages,
    int CommerceId,
    string CommerceName,
    IReadOnlyList<CommerceTransactionDto> Data
);
