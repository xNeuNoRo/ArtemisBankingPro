using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Queries;

public sealed record GetAccountTransactionsQuery(
    string AccountNumber,
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize
) : IRequest<Result<AccountDetailDto>>, IAuthorize {
    public string[] RequiredRoles => ["Administrador"];
}
