namespace ArtemisBankingPro.Application.Features.CreditCard.DTOs;

/// <summary>Consumo realizado con una tarjeta de crédito (compra o avance de efectivo).</summary>
public sealed record CardConsumptionDto(
    int Id,
    DateTimeOffset Date,
    decimal Amount,
    string CommerceName,
    string Status
);
