namespace ArtemisBankingPro.Application.Features.Merchants.Requests;

public sealed record CreateMerchantApiRequest(
    string? Name,
    string? Description,
    string? Email,
    string? PhoneNumber,
    string? Rnc
);

public sealed record UpdateMerchantApiRequest(
    string? Name,
    string? Description,
    string? Email,
    string? PhoneNumber,
    string? Rnc
);

public sealed record ChangeMerchantStatusApiRequest(bool? Status);
