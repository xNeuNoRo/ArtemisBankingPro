using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Queries;

/// <summary>
/// Listado paginado de las operaciones iniciadas por un cajero, con filtros
/// por tipo, estado y rango de fechas de negocio.
/// </summary>
public sealed record GetCashierOperationsPagedQuery(
    string CashierId,
    CashierOperationFilters? Filters = null,
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize
) : IRequest<Result<PageResult<CashierOperationDto>>>, IAuthorize {
    public string[] RequiredRoles => ["Cajero"];
}
