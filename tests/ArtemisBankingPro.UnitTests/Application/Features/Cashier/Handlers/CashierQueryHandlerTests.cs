using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Features.Cashier.Handlers;
using ArtemisBankingPro.Application.Features.Cashier.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Operations.Enums;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Cashier.Handlers;

public sealed class GetCashierDashboardQueryHandlerTests {
    private static readonly DateOnly BusinessDate = new(2026, 8, 6);

    private static CashierDashboardDto SampleDashboard() =>
        new() {
            TotalTransactions = 7,
            PaymentsToday = 1_600m,
            DepositsToday = 3,
            WithdrawalsToday = 2,
        };

    [Fact]
    public async Task Handle_ReturnsDashboardFromRepository() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetDashboardAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleDashboard());

        var handler = new GetCashierDashboardQueryHandler(repository.Object);

        var result = await handler.Handle(
            new GetCashierDashboardQuery("cashier-1", BusinessDate),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalTransactions.Should().Be(7);
        result.Value.PaymentsToday.Should().Be(1_600m);
        result.Value.DepositsToday.Should().Be(3);
        result.Value.WithdrawalsToday.Should().Be(2);
    }

    [Fact]
    public async Task Handle_PassesCashierIdAndBusinessDateToRepository() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetDashboardAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashierDashboardDto());

        var handler = new GetCashierDashboardQueryHandler(repository.Object);

        await handler.Handle(
            new GetCashierDashboardQuery("cashier-9", BusinessDate),
            CancellationToken.None
        );

        repository.Verify(
            r => r.GetDashboardAsync("cashier-9", BusinessDate, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_NoTransactions_ReturnsZeroedDashboard() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetDashboardAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashierDashboardDto());

        var handler = new GetCashierDashboardQueryHandler(repository.Object);

        var result = await handler.Handle(
            new GetCashierDashboardQuery("unknown-cashier", BusinessDate),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalTransactions.Should().Be(0);
        result.Value.PaymentsToday.Should().Be(0m);
        result.Value.DepositsToday.Should().Be(0);
        result.Value.WithdrawalsToday.Should().Be(0);
    }

    [Fact]
    public async Task Handle_RepeatedInvocation_ReturnsIdenticalResultWithoutStateChanges() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetDashboardAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleDashboard());

        var handler = new GetCashierDashboardQueryHandler(repository.Object);

        var first = await handler.Handle(
            new GetCashierDashboardQuery("cashier-1", BusinessDate),
            CancellationToken.None
        );
        var second = await handler.Handle(
            new GetCashierDashboardQuery("cashier-1", BusinessDate),
            CancellationToken.None
        );

        second.Value.Should().BeEquivalentTo(first.Value);
        repository.Verify(
            r => r.GetDashboardAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2)
        );
    }

    [Fact]
    public void Query_RequiresCajeroRole() {
        var query = new GetCashierDashboardQuery("cashier-1", BusinessDate);

        query.RequiredRoles.Should().Equal("Cajero");
        (query is IAuthorize).Should().BeTrue();
    }
}

public sealed class GetCashierOperationsPagedQueryHandlerTests {
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private static PageResult<CashierOperationDto> SamplePage() =>
        new(
            [
                new CashierOperationDto(
                    Guid.NewGuid(),
                    "Deposit",
                    "Approved",
                    500m,
                    OccurredAt,
                    "0001",
                    null,
                    null,
                    null
                ),
                new CashierOperationDto(
                    Guid.NewGuid(),
                    "Withdrawal",
                    "Rejected",
                    200m,
                    OccurredAt,
                    "0002",
                    null,
                    null,
                    "InsufficientFunds"
                ),
            ],
            TotalCount: 2,
            Page: 1,
            PageSize: 20
        );

    [Fact]
    public async Task Handle_ReturnsPagedOperationsFromRepository() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetOperationsPagedAsync(It.IsAny<string>(), It.IsAny<CashierOperationFilters>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SamplePage());

        var handler = new GetCashierOperationsPagedQueryHandler(repository.Object);

        var result = await handler.Handle(
            new GetCashierOperationsPagedQuery("cashier-1"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items[0].Kind.Should().Be("Deposit");
        result.Value.Items[0].Amount.Should().Be(500m);
        result.Value.Items[0].AccountLastFour.Should().Be("0001");
        result.Value.Items[1].Status.Should().Be("Rejected");
        result.Value.Items[1].RejectionCode.Should().Be("InsufficientFunds");
    }

    [Fact]
    public async Task Handle_PassesFiltersAndPagedContractToRepository() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetOperationsPagedAsync(It.IsAny<string>(), It.IsAny<CashierOperationFilters>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResult<CashierOperationDto>([], TotalCount: 0, Page: 1, PageSize: 20));

        var filters = new CashierOperationFilters(
            FinancialOperationKind.Deposit,
            FinancialOperationStatus.Approved
        );

        var handler = new GetCashierOperationsPagedQueryHandler(repository.Object);

        await handler.Handle(
            new GetCashierOperationsPagedQuery("cashier-1", filters, Page: 2, PageSize: 15),
            CancellationToken.None
        );

        repository.Verify(
            r => r.GetOperationsPagedAsync(
                "cashier-1",
                It.Is<CashierOperationFilters>(value => value == filters),
                It.Is<PageRequest>(value => value.Page == 2 && value.PageSize == 15),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_NullFilters_PassesDefaultFilters() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetOperationsPagedAsync(It.IsAny<string>(), It.IsAny<CashierOperationFilters>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResult<CashierOperationDto>([], TotalCount: 0, Page: 1, PageSize: 20));

        CashierOperationFilters defaultFilters = new();

        var handler = new GetCashierOperationsPagedQueryHandler(repository.Object);

        await handler.Handle(new GetCashierOperationsPagedQuery("cashier-1"), CancellationToken.None);

        repository.Verify(
            r => r.GetOperationsPagedAsync(
                "cashier-1",
                It.Is<CashierOperationFilters>(value => value == defaultFilters),
                It.IsAny<PageRequest>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsEmptyPage() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetOperationsPagedAsync(It.IsAny<string>(), It.IsAny<CashierOperationFilters>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResult<CashierOperationDto>([], TotalCount: 0, Page: 1, PageSize: 20));

        var handler = new GetCashierOperationsPagedQueryHandler(repository.Object);

        var result = await handler.Handle(
            new GetCashierOperationsPagedQuery("unknown-cashier"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_RepeatedInvocation_ReturnsIdenticalResultWithoutStateChanges() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetOperationsPagedAsync(It.IsAny<string>(), It.IsAny<CashierOperationFilters>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SamplePage());

        var handler = new GetCashierOperationsPagedQueryHandler(repository.Object);

        var first = await handler.Handle(
            new GetCashierOperationsPagedQuery("cashier-1"),
            CancellationToken.None
        );
        var second = await handler.Handle(
            new GetCashierOperationsPagedQuery("cashier-1"),
            CancellationToken.None
        );

        second.Value.Should().BeEquivalentTo(first.Value);
        repository.Verify(
            r => r.GetOperationsPagedAsync(It.IsAny<string>(), It.IsAny<CashierOperationFilters>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2)
        );
    }

    [Fact]
    public void Query_RequiresCajeroRole() {
        var query = new GetCashierOperationsPagedQuery("cashier-1");

        query.RequiredRoles.Should().Equal("Cajero");
        (query is IAuthorize).Should().BeTrue();
    }
}
