namespace ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;

public sealed record SavingsAccountResponse(
    int AccountId,
    string AccountNumber,
    string ClientId,
    string ClientFullName,
    decimal Balance,
    string Type,
    string Status,
    DateTimeOffset CreatedAt
);
