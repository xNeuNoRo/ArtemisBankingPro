namespace ArtemisBankingPro.Application.Features.Merchants.DTOs;

/// <summary>
/// Read projection used to enrich the administrative commerce-user query.
/// </summary>
public sealed record MerchantUserAssociationDto(
    string UserId,
    int CommerceId,
    string CommerceName
);
