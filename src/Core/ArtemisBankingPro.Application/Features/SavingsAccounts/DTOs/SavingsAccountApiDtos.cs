namespace ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;

public sealed record SavingsAccountApiDto(
    string Id,
    string AccountNumber,
    string ClientId,
    string ClientFullName,
    string Identification,
    decimal Balance,
    string Type,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed record CreateSavingsAccountApiResponse(
    string Id,
    string AccountNumber,
    string ClientId,
    string ClientFullName,
    decimal Balance,
    string Type,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed record SavingsAccountApiTransactionDto(
    string Id,
    DateTimeOffset Date,
    decimal Amount,
    string TransactionType,
    string Origin,
    string Beneficiary,
    string Status
);

public sealed record SavingsAccountApiTransactionPageDto(
    int Page,
    int PageSize,
    int TotalRecords,
    int TotalPages,
    IReadOnlyList<SavingsAccountApiTransactionDto> Data
);

public sealed record SavingsAccountApiDetailDto(
    string AccountNumber,
    string ClientFullName,
    decimal Balance,
    string Type,
    string Status,
    SavingsAccountApiTransactionPageDto Transactions
);
