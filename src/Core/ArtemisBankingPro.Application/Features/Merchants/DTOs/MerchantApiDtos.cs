namespace ArtemisBankingPro.Application.Features.Merchants.DTOs;

public sealed record MerchantApiSummaryDto(
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

public sealed record MerchantApiUserDto(
    string Id,
    string UserName,
    string Email,
    bool IsActive
);

public sealed record MerchantApiDetailDto(
    int Id,
    string Name,
    string? Description,
    string Email,
    string PhoneNumber,
    string Rnc,
    bool IsActive,
    DateTimeOffset CreatedAt,
    MerchantApiUserDto? AssociatedUser
);

public sealed record CreateMerchantApiResponse(
    int Id,
    string Name,
    string? Description,
    string Email,
    string PhoneNumber,
    string Rnc,
    bool IsActive,
    DateTimeOffset CreatedAt
);
