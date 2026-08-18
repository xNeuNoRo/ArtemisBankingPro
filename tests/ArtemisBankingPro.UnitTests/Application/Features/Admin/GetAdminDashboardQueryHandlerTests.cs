using ArtemisBankingPro.Application.Features.Admin.DTOs;
using ArtemisBankingPro.Application.Features.Admin.Handlers;
using ArtemisBankingPro.Application.Features.Admin.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Admin;

/// <summary>
/// Verifica la composición de los once indicadores del dashboard
/// administrativo (spec §16-§18): el handler combina los agregados del
/// repositorio y aplica la fórmula de deuda promedio solo sobre clientes
/// activos, con redondeo centralizado en <see cref="Money"/>.
/// </summary>
public sealed class GetAdminDashboardQueryHandlerTests {
    private static readonly DateOnly FixedToday = new(2026, 8, 7);

    private static (
        GetAdminDashboardQueryHandler Handler,
        Mock<IAdminRepository> AdminRepository,
        Mock<IUserRepository> UserRepository
    ) CreateHandler(
        OperationDashboardCounts operations,
        ClientStatusCounts clients,
        int activeLoans,
        int activeCreditCards,
        int activeSavingsAccounts,
        Money? activeClientDebt = null,
        IReadOnlyList<string>? activeClientIds = null
    ) {
        var adminRepository = new Mock<IAdminRepository>();
        adminRepository
            .Setup(repository => repository.GetOperationCountsAsync(
                FixedToday,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations);
        adminRepository
            .Setup(repository => repository.CountActiveLoansAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeLoans);
        adminRepository
            .Setup(repository =>
                repository.CountActiveCreditCardsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeCreditCards);
        adminRepository
            .Setup(repository =>
                repository.CountActiveSavingsAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeSavingsAccounts);
        if (activeClientDebt is not null) {
            adminRepository
                .Setup(repository => repository.GetActiveClientDebtAsync(
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeClientDebt);
        }

        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(repository =>
                repository.GetClientStatusCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(clients);
        if (activeClientIds is not null) {
            userRepository
                .Setup(repository =>
                    repository.GetActiveClientIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeClientIds);
        }

        var clock = new Mock<IBusinessClock>();
        clock.Setup(clock => clock.Today).Returns(FixedToday);

        return (
            new GetAdminDashboardQueryHandler(
                adminRepository.Object,
                userRepository.Object,
                clock.Object
            ),
            adminRepository,
            userRepository
        );
    }

    [Fact]
    public async Task Handle_ComposesAllIndicators() {
        var (handler, _, _) = CreateHandler(
            operations: new OperationDashboardCounts(
                TotalTransactions: 10,
                TodayTransactions: 2,
                TotalPayments: 3,
                TodayPayments: 1
            ),
            clients: new ClientStatusCounts(Active: 4, Inactive: 2),
            activeLoans: 5,
            activeCreditCards: 6,
            activeSavingsAccounts: 7,
            activeClientDebt: Money.Create(10_000m).Value,
            activeClientIds: ["cliente-a", "cliente-b", "cliente-c", "cliente-d"]
        );

        var result = await handler.Handle(new GetAdminDashboardQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        AdminDashboardDto dashboard = result.Value;

        dashboard.TotalTransactionsHistorical.Should().Be(10);
        dashboard.TransactionsToday.Should().Be(2);
        dashboard.TotalPaymentsHistorical.Should().Be(3);
        dashboard.PaymentsToday.Should().Be(1);
        dashboard.ActiveClients.Should().Be(4);
        dashboard.InactiveClients.Should().Be(2);
        dashboard.TotalFinancialProducts.Should().Be(18); // 7 cuentas + 5 préstamos + 6 tarjetas.
        dashboard.ActiveLoans.Should().Be(5);
        dashboard.ActiveCreditCards.Should().Be(6);
        dashboard.ActiveSavingsAccounts.Should().Be(7);
        dashboard.AverageDebtPerClient.Should().Be(2_500.00m); // RD$10,000.00 / 4 clientes.
    }

    [Fact]
    public async Task Handle_NoActiveClients_ReturnsZeroAverageDebt_AndSkipsDebtQueries() {
        var (handler, adminRepository, userRepository) = CreateHandler(
            operations: new OperationDashboardCounts(1, 1, 0, 0),
            clients: new ClientStatusCounts(Active: 0, Inactive: 5),
            activeLoans: 1,
            activeCreditCards: 1,
            activeSavingsAccounts: 1
        );

        var result = await handler.Handle(new GetAdminDashboardQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AverageDebtPerClient.Should().Be(0m);

        // Sin clientes activos no se consulta deuda (spec §568-569: RD$0.00).
        userRepository.Verify(
            repository => repository.GetActiveClientIdsAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
        adminRepository.Verify(
            repository => repository.GetActiveClientDebtAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_RoundsAverageDebtToTwoDecimals() {
        var (handler, _, _) = CreateHandler(
            operations: new OperationDashboardCounts(0, 0, 0, 0),
            clients: new ClientStatusCounts(Active: 3, Inactive: 0),
            activeLoans: 0,
            activeCreditCards: 0,
            activeSavingsAccounts: 0,
            activeClientDebt: Money.Create(10_000m).Value,
            activeClientIds: ["cliente-a", "cliente-b", "cliente-c"]
        );

        var result = await handler.Handle(new GetAdminDashboardQuery(), CancellationToken.None);

        // 10,000 / 3 = 3,333.333... -> 3,333.33 (redondeo centralizado de Money).
        result.Value.AverageDebtPerClient.Should().Be(3_333.33m);
    }

    [Fact]
    public async Task Handle_PassesBusinessTodayToOperationCounts() {
        var (handler, adminRepository, _) = CreateHandler(
            operations: new OperationDashboardCounts(0, 0, 0, 0),
            clients: new ClientStatusCounts(Active: 1, Inactive: 0),
            activeLoans: 0,
            activeCreditCards: 0,
            activeSavingsAccounts: 0,
            activeClientDebt: Money.Zero,
            activeClientIds: ["cliente-a"]
        );

        await handler.Handle(new GetAdminDashboardQuery(), CancellationToken.None);

        // "Transacciones del día" y "pagos del día" usan la fecha de negocio (spec §578-580).
        adminRepository.Verify(
            repository => repository.GetOperationCountsAsync(
                FixedToday,
                It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public void Query_RequiresAdministradorRole() {
        var query = new GetAdminDashboardQuery();

        Assert.Equal(["Administrador"], query.RequiredRoles);
    }
}
