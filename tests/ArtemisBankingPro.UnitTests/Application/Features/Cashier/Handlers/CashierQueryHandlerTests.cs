using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Features.Cashier.Handlers;
using ArtemisBankingPro.Application.Features.Cashier.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Operations.Enums;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Cashier.Handlers;

public sealed class GetCashierDashboardQueryHandlerTests {
    private static readonly DateOnly BusinessDate = new(2026, 8, 6);

    private static Mock<IBusinessClock> Clock(DateOnly today) {
        var clock = new Mock<IBusinessClock>();
        clock.SetupGet(c => c.Today).Returns(today);
        return clock;
    }

    private static Mock<ICurrentUserService> CurrentUser(string userId = "cashier-1") {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(userId);
        return currentUser;
    }

    private static CashierDashboardDto SampleDashboard() =>
        new(TransactionsToday: 7, PaymentsToday: 2, DepositsToday: 3, WithdrawalsToday: 2);

    [Fact]
    public async Task Handle_ReturnsDashboardFromRepository() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetDashboardAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleDashboard());

        var handler = new GetCashierDashboardQueryHandler(
            repository.Object,
            CurrentUser().Object,
            Clock(BusinessDate).Object
        );

        var result = await handler.Handle(new GetCashierDashboardQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TransactionsToday.Should().Be(7);
        result.Value.PaymentsToday.Should().Be(2);
        result.Value.DepositsToday.Should().Be(3);
        result.Value.WithdrawalsToday.Should().Be(2);
    }

    [Fact]
    public async Task Handle_UsesAuthenticatedCashierIdAndBusinessToday() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetDashboardAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashierDashboardDto(0, 0, 0, 0));

        var handler = new GetCashierDashboardQueryHandler(
            repository.Object,
            CurrentUser("auth-cashier-42").Object,
            Clock(BusinessDate).Object
        );

        await handler.Handle(new GetCashierDashboardQuery(), CancellationToken.None);

        repository.Verify(
            r => r.GetDashboardAsync("auth-cashier-42", BusinessDate, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_NoTransactions_ReturnsZeroedDashboard() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetDashboardAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashierDashboardDto(0, 0, 0, 0));

        var handler = new GetCashierDashboardQueryHandler(
            repository.Object,
            CurrentUser().Object,
            Clock(BusinessDate).Object
        );

        var result = await handler.Handle(new GetCashierDashboardQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TransactionsToday.Should().Be(0);
        result.Value.PaymentsToday.Should().Be(0);
        result.Value.DepositsToday.Should().Be(0);
        result.Value.WithdrawalsToday.Should().Be(0);
    }

    [Fact]
    public async Task Handle_RepeatedInvocation_ReturnsIdenticalResultWithoutStateChanges() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetDashboardAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleDashboard());

        var handler = new GetCashierDashboardQueryHandler(
            repository.Object,
            CurrentUser().Object,
            Clock(BusinessDate).Object
        );

        var first = await handler.Handle(new GetCashierDashboardQuery(), CancellationToken.None);
        var second = await handler.Handle(new GetCashierDashboardQuery(), CancellationToken.None);

        Assert.Equivalent(first.Value, second.Value);
        repository.Verify(
            r => r.GetDashboardAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2)
        );
    }

    [Fact]
    public void Query_RequiresCajeroRole() {
        var query = new GetCashierDashboardQuery();

        query.RequiredRoles.Should().Equal("Cajero");
        (query is IAuthorize).Should().BeTrue();
    }
}

public sealed class GetCashierOperationsQueryHandlerTests {
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private static Mock<ICurrentUserService> CurrentUser(string userId = "cashier-1") {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(userId);
        return currentUser;
    }

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
    public async Task Handle_ReturnsPagedOperationsForAuthenticatedCashier() {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetOperationsPagedAsync(It.IsAny<string>(), It.IsAny<CashierOperationFilters>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SamplePage());

        var handler = new GetCashierOperationsQueryHandler(repository.Object, CurrentUser().Object);

        var result = await handler.Handle(new GetCashierOperationsQuery(), CancellationToken.None);

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
    public async Task Handle_PassesAuthenticatedCashierIdAndFiltersToRepository() {
        DateTimeOffset from = new(2026, 8, 1, 4, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = new(2026, 8, 6, 4, 0, 0, TimeSpan.Zero);

        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetOperationsPagedAsync(It.IsAny<string>(), It.IsAny<CashierOperationFilters>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResult<CashierOperationDto>([], TotalCount: 0, Page: 1, PageSize: 20));

        var handler = new GetCashierOperationsQueryHandler(
            repository.Object,
            CurrentUser("cashier-9").Object
        );

        await handler.Handle(
            new GetCashierOperationsQuery(from, to, "Deposit", Page: 2, PageSize: 15),
            CancellationToken.None
        );

        repository.Verify(
            r => r.GetOperationsPagedAsync(
                "cashier-9",
                It.Is<CashierOperationFilters>(filters =>
                    filters.DateFrom == from
                    && filters.DateTo == to
                    && filters.Kind == FinancialOperationKind.Deposit
                ),
                It.Is<PageRequest>(page => page.Page == 2 && page.PageSize == 15),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Theory]
    [InlineData("Deposit", FinancialOperationKind.Deposit)]
    [InlineData("Withdrawal", FinancialOperationKind.Withdrawal)]
    [InlineData("CardPayment", FinancialOperationKind.CreditCardPayment)]
    [InlineData("LoanPayment", FinancialOperationKind.LoanPayment)]
    [InlineData("ThirdPartyTransfer", FinancialOperationKind.CashierTransfer)]
    [InlineData("thirdpartytransfer", FinancialOperationKind.CashierTransfer)]
    [InlineData(null, null)]
    public async Task Handle_OperationType_MapsToRepositoryKind(
        string? operationType,
        FinancialOperationKind? expectedKind
    ) {
        var repository = new Mock<ICashierRepository>();
        repository
            .Setup(r => r.GetOperationsPagedAsync(It.IsAny<string>(), It.IsAny<CashierOperationFilters>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResult<CashierOperationDto>([], TotalCount: 0, Page: 1, PageSize: 20));

        var handler = new GetCashierOperationsQueryHandler(repository.Object, CurrentUser().Object);

        await handler.Handle(
            new GetCashierOperationsQuery(OperationType: operationType),
            CancellationToken.None
        );

        repository.Verify(
            r => r.GetOperationsPagedAsync(
                It.IsAny<string>(),
                It.Is<CashierOperationFilters>(filters => filters.Kind == expectedKind),
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

        var handler = new GetCashierOperationsQueryHandler(repository.Object, CurrentUser().Object);

        var result = await handler.Handle(new GetCashierOperationsQuery(), CancellationToken.None);

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

        var handler = new GetCashierOperationsQueryHandler(repository.Object, CurrentUser().Object);

        var first = await handler.Handle(new GetCashierOperationsQuery(), CancellationToken.None);
        var second = await handler.Handle(new GetCashierOperationsQuery(), CancellationToken.None);

        Assert.Equivalent(first.Value, second.Value);
        repository.Verify(
            r => r.GetOperationsPagedAsync(It.IsAny<string>(), It.IsAny<CashierOperationFilters>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2)
        );
    }

    [Fact]
    public void Query_RequiresCajeroRole() {
        var query = new GetCashierOperationsQuery();

        query.RequiredRoles.Should().Equal("Cajero");
        (query is IAuthorize).Should().BeTrue();
    }
}
