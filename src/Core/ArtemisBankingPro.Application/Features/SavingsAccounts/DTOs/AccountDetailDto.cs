using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;

public sealed record AccountDetailDto(
    string AccountNumber,
    string ClientFullName,
    decimal Balance,
    string Type,
    string Status,
    PageResult<AccountTransactionDto> Transactions
);
