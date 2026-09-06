using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.Application.Features.CreditCard.DTOs;

/// <summary>Detalle de tarjeta de crédito con consumos paginados.</summary>
public sealed record CreditCardDetailDto(
    int Id,
    string MaskedNumber,
    string LastFour,
    string ClientFullName,
    decimal CreditLimit,
    decimal AvailableCredit,
    decimal CurrentDebt,
    string Expiration,
    string Status,
    PageResult<CardConsumptionDto> Consumptions
);
