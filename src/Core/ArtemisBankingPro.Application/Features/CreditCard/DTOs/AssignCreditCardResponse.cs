namespace ArtemisBankingPro.Application.Features.CreditCard.DTOs;

/// <summary>Respuesta de asignación de tarjeta de crédito.</summary>
public sealed record AssignCreditCardResponse(
    int CardId,
    string MaskedNumber,
    string LastFour,
    string ClientId,
    string ClientFullName,
    decimal CreditLimit,
    decimal AvailableCredit,
    string Expiration,
    string Status,
    DateTimeOffset CreatedAt
);