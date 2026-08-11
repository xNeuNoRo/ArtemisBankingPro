using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Features.Cashier.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Devuelve los indicadores diarios del cajero para la fecha de negocio
/// solicitada.
/// </summary>
public sealed class GetCashierDashboardQueryHandler
    : IRequestHandler<GetCashierDashboardQuery, Result<CashierDashboardDto>> {
    private readonly ICashierRepository _cashierRepository;

    public GetCashierDashboardQueryHandler(ICashierRepository cashierRepository) {
        _cashierRepository = cashierRepository;
    }

    public async ValueTask<Result<CashierDashboardDto>> Handle(
        GetCashierDashboardQuery message,
        CancellationToken cancellationToken
    ) {
        CashierDashboardDto dashboard = await _cashierRepository.GetDashboardAsync(
            message.CashierId,
            message.Date,
            cancellationToken
        );

        return Result.Success(dashboard);
    }
}
