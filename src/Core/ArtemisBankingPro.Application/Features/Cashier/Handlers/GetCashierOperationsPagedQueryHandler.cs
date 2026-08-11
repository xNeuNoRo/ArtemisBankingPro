using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Features.Cashier.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Lista las operaciones del cajero aplicando los filtros tipados y la
/// paginación del contrato.
/// </summary>
public sealed class GetCashierOperationsPagedQueryHandler
    : IRequestHandler<GetCashierOperationsPagedQuery, Result<PageResult<CashierOperationDto>>> {
    private readonly ICashierRepository _cashierRepository;

    public GetCashierOperationsPagedQueryHandler(ICashierRepository cashierRepository) {
        _cashierRepository = cashierRepository;
    }

    public async ValueTask<Result<PageResult<CashierOperationDto>>> Handle(
        GetCashierOperationsPagedQuery message,
        CancellationToken cancellationToken
    ) {
        PageRequest page = new(message.Page, message.PageSize);
        CashierOperationFilters filters = message.Filters ?? new CashierOperationFilters();

        PageResult<CashierOperationDto> paged = await _cashierRepository.GetOperationsPagedAsync(
            message.CashierId,
            filters,
            page,
            cancellationToken
        );

        return Result.Success(paged);
    }
}
