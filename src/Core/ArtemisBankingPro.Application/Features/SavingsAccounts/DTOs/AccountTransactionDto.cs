namespace ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;

public sealed record AccountTransactionDto(
    int Id,
    DateTimeOffset Date,
    decimal Amount,
    string TransactionType,
    string Origin,
    string Beneficiary,
    string Status
);
