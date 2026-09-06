using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Queries;

/// <summary>
/// Historial paginado de las operaciones iniciadas por el cajero autenticado,
/// del más reciente al más antiguo, con filtros por rango de fechas y tipo de
/// operación. El cajero se deriva del actor autenticado; nunca de entrada del
/// cliente.
/// </summary>
public sealed record GetCashierOperationsQuery(
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null,
    string? OperationType = null,
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize
) : IRequest<Result<PageResult<CashierOperationDto>>>, IAuthorize {
    public string[] RequiredRoles => ["Cajero"];
}
