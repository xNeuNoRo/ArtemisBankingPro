namespace ArtemisBankingPro.Application.Features.CreditCard.DTOs;

/// <summary>Resumen de tarjeta de crédito para listados.</summary>
public sealed record CreditCardSummeryDto(
    int Id,
    string MaskedNumber,
    string LastFour,
    string ClientId,
    string ClientFullName,
    decimal CreditLimit,
    decimal AvailableCredit,
    decimal CurrentDebt,
    string Status,
    string Expiration,
    DateTimeOffset CreatedAt
);
