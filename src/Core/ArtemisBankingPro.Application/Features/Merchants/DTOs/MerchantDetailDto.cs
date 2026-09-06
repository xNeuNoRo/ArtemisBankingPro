namespace ArtemisBankingPro.Application.Features.Merchants.DTOs;

/// <summary>
/// Detalle de un comercio (spec §40, GET /api/commerce/{id}).
/// </summary>
public sealed record MerchantDetailDto(
    int Id,
    string Name,
    string? Description,
    string Email,
    string PhoneNumber,
    string Rnc,
    bool IsActive,
    DateTimeOffset CreatedAt,
    MerchantUserDto? AssociatedUser
);
