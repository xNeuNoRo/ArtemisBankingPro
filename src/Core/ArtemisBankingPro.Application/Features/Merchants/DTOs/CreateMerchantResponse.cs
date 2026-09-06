namespace ArtemisBankingPro.Application.Features.Merchants.DTOs;

/// <summary>
/// Comercio creado (spec §40, POST /api/commerce, respuesta 201).
/// </summary>
public sealed record CreateMerchantResponse(
    int Id,
    string Name,
    string? Description,
    string Email,
    string PhoneNumber,
    string Rnc,
    bool IsActive,
    DateTimeOffset CreatedAt
);
