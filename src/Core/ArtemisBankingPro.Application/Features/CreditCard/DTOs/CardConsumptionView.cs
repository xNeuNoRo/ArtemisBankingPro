using ArtemisBankingPro.Domain.Operations.Enums;

namespace ArtemisBankingPro.Application.Features.CreditCard.DTOs;

/// <summary>
/// Read model for a credit-card consumption query.
/// </summary>
public sealed record CardConsumptionView(
    int Id,
    DateTimeOffset Date,
    decimal Amount,
    string CommerceName,
    FinancialOperationStatus Status
);
