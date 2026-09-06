using System.Text.Json.Serialization;

namespace ArtemisBankingPro.Application.Features.CreditCard.DTOs;

public sealed record CreditCardApiCardDto(
    string Id,
    [property: JsonPropertyName("maskedCardNumber")] string MaskedNumber,
    string LastFourDigits,
    string ClientId,
    string ClientFullName,
    decimal CreditLimit,
    decimal AvailableCredit,
    decimal CurrentDebt,
    string ExpirationDate,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed record CreditCardApiConsumptionDto(
    string Id,
    DateTimeOffset Date,
    decimal Amount,
    string CommerceName,
    string Status
);

public sealed record CreditCardApiDetailDto(
    string Id,
    [property: JsonPropertyName("maskedCardNumber")] string MaskedNumber,
    string LastFourDigits,
    string ClientId,
    string ClientFullName,
    decimal CreditLimit,
    decimal AvailableCredit,
    decimal CurrentDebt,
    string ExpirationDate,
    string Status,
    IReadOnlyList<CreditCardApiConsumptionDto> Consumptions
);
