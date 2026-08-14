using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Queries;

public sealed record GetMyAccountTransactionsQuery(
    string AccountNumber,
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null,
    string? TransactionType = null,
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize
) : IRequest<Result<PageResult<AccountTransactionDto>>>, IAuthorize {
    public string[] RequiredRoles => ["Cliente"];
}
