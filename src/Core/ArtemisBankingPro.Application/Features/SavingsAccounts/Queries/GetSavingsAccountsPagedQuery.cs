using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Queries;

public sealed record GetSavingsAccountsPagedQuery(
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize,
    string? Status = "activa",
    string? Type = null,
    string? Identification = null
) : IRequest<Result<PageResult<SavingsAccountSummaryDto>>>, IAuthorize {
    public string[] RequiredRoles => ["Administrador"];
}
