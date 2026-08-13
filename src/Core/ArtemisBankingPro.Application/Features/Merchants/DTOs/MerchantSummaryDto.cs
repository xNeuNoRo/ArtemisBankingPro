namespace ArtemisBankingPro.Application.Features.Merchants.DTOs;

/// <summary>
/// Resumen de un comercio para el listado paginado (spec §40, GET /api/commerce).
/// </summary>
public sealed record MerchantSummaryDto(
    int Id,
    string Name,
    string? Description,
    string Email,
    string PhoneNumber,
    string Rnc,
    bool IsActive,
    bool HasAssociatedUser,
    DateTimeOffset CreatedAt
);
