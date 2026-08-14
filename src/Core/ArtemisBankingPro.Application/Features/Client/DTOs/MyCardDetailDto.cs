using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.Application.Features.Client.DTOs;

public sealed record MyCardDetailDto(
    int CardId,
    string LastFour,
    decimal CreditLimit,
    decimal AvailableCredit,
    decimal CurrentDebt,
    string Expiration,
    PageResult<MyCardConsumptionDto> Consumptions
);

public sealed record MyCardConsumptionDto(
    int Id,
    DateTimeOffset Date,
    decimal Amount,
    string CommerceName,
    string Status
);
