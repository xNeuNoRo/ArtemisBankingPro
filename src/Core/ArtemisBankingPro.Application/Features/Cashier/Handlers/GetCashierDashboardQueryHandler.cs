using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Features.Cashier.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Devuelve los indicadores diarios del cajero autenticado. El cajero proviene
/// del token (nunca de entrada del cliente) y la fecha de negocio del reloj
/// empresarial configurado.
/// </summary>
public sealed class GetCashierDashboardQueryHandler
    : IRequestHandler<GetCashierDashboardQuery, Result<CashierDashboardDto>> {
    private readonly ICashierRepository _cashierRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IBusinessClock _clock;

    public GetCashierDashboardQueryHandler(
        ICashierRepository cashierRepository,
        ICurrentUserService currentUser,
        IBusinessClock clock
    ) {
        _cashierRepository = cashierRepository;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async ValueTask<Result<CashierDashboardDto>> Handle(
        GetCashierDashboardQuery message,
        CancellationToken cancellationToken
    ) {
        CashierDashboardDto dashboard = await _cashierRepository.GetDashboardAsync(
            _currentUser.UserId!,
            _clock.Today,
            cancellationToken
        );

        return Result.Success(dashboard);
    }
}
