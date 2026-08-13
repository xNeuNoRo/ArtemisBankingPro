namespace ArtemisBankingPro.Application.Features.Merchants.DTOs;

/// <summary>
/// Usuario asociado a un comercio (spec §40, GET /api/commerce/{id}).
/// </summary>
public sealed record MerchantUserDto(
    string Id,
    string UserName,
    string Email,
    bool IsActive
);
