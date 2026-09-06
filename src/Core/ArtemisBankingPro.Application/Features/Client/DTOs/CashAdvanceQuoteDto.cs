namespace ArtemisBankingPro.Application.Features.Client.DTOs;

public sealed record CashAdvanceQuoteDto(
    decimal PrincipalAmount,
    decimal InterestAmount,
    decimal TotalToCharge,
    decimal AvailableCredit,
    bool IsEligible
);
