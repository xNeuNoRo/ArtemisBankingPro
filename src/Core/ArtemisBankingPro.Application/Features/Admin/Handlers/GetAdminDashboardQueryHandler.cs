using ArtemisBankingPro.Application.Features.Admin.DTOs;
using ArtemisBankingPro.Application.Features.Admin.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Admin.Handlers;

/// <summary>
/// Compone los indicadores del dashboard administrativo (spec §16-§18):
/// agregados de operaciones y productos del repositorio administrativo más
/// los conteos de clientes de Identity. La deuda promedio se calcula
/// únicamente sobre clientes activos y con el redondeo centralizado de
/// <see cref="Money"/>; si no existen clientes activos el promedio es RD$0.00.
/// </summary>
public sealed class GetAdminDashboardQueryHandler
    : IRequestHandler<GetAdminDashboardQuery, Result<AdminDashboardDto>> {
    private readonly IAdminRepository _adminRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBusinessClock _clock;

    public GetAdminDashboardQueryHandler(
        IAdminRepository adminRepository,
        IUserRepository userRepository,
        IBusinessClock clock
    ) {
        _adminRepository = adminRepository;
        _userRepository = userRepository;
        _clock = clock;
    }

    public async ValueTask<Result<AdminDashboardDto>> Handle(
        GetAdminDashboardQuery message,
        CancellationToken cancellationToken
    ) {
        // "Transacciones del día" y "pagos del día" usan la fecha de negocio
        // configurada (spec §578-580), nunca DateTime.Now.
        OperationDashboardCounts operations = await _adminRepository.GetOperationCountsAsync(
            _clock.Today,
            cancellationToken
        );

        ClientStatusCounts clients = await _userRepository.GetClientStatusCountsAsync(
            cancellationToken
        );

        int activeLoans = await _adminRepository.CountActiveLoansAsync(cancellationToken);
        int activeCreditCards = await _adminRepository.CountActiveCreditCardsAsync(
            cancellationToken
        );
        int activeSavingsAccounts = await _adminRepository.CountActiveSavingsAccountsAsync(
            cancellationToken
        );

        // Fórmula spec §565-569: deuda total de clientes activos / clientes
        // activos. Sin clientes activos, RD$0.00 (y no se consulta deuda).
        decimal averageDebtPerClient = 0m;
        if (clients.Active > 0) {
            IReadOnlyList<string> activeClientIds = await _userRepository.GetActiveClientIdsAsync(
                cancellationToken
            );
            Money activeClientDebt = await _adminRepository.GetActiveClientDebtAsync(
                activeClientIds,
                cancellationToken
            );

            // Money.Create centraliza el redondeo a dos decimales.
            averageDebtPerClient = Money.Create(
                activeClientDebt.Amount / clients.Active
            ).Value.Amount;
        }

        return Result.Success(
            new AdminDashboardDto(
                operations.TotalTransactions,
                operations.TodayTransactions,
                operations.TotalPayments,
                operations.TodayPayments,
                clients.Active,
                clients.Inactive,
                activeSavingsAccounts + activeLoans + activeCreditCards,
                activeLoans,
                activeCreditCards,
                activeSavingsAccounts,
                averageDebtPerClient
            )
        );
    }
}
