namespace ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;

public sealed record SavingsAccountSummaryDto(
    int Id,
    string AccountNumber,
    string ClientId,
    string ClientFullName,
    string Identification,
    decimal Balance,
    string Type,
    string Status,
    DateTimeOffset CreatedAt
);
