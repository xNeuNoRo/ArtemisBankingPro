using ArtemisBankingPro.Domain.Operations.Enums;

namespace ArtemisBankingPro.Domain.Cards.Details;

/// <summary>
/// Proyección de un consumo de tarjeta para consultas de detalle.
/// El nombre de comercio ya se resuelve ("AVANCE" para avances de efectivo).
/// </summary>
public sealed record CardConsumptionView(
    int Id,
    DateTimeOffset Date,
    decimal Amount,
    string CommerceName,
    FinancialOperationStatus Status
);
