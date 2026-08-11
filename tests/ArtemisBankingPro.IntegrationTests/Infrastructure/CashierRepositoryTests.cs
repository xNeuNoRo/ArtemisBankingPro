using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class CashierRepositoryTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string Cashier = "cashier-1";
    private static readonly DateOnly BusinessDate = new(2026, 8, 6);

    // Día de negocio 2026-08-06 en America/Santo_Domingo (UTC-4):
    // [2026-08-06T04:00:00Z, 2026-08-07T04:00:00Z)
    private static readonly DateTimeOffset DayStartUtc = new(2026, 8, 6, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayEndUtc = new(2026, 8, 7, 4, 0, 0, TimeSpan.Zero);

    private static AccountNumber Account(string number) => AccountNumber.Create(number).Value;

    private static FinancialOperation NewDeposit(
        decimal amount,
        string account = "400000001",
        DateTimeOffset? occurredAt = null
    ) =>
        FinancialOperation
            .Approve(
                Guid.NewGuid(),
                FinancialOperationKind.Deposit,
                Money.Create(amount).Value,
                Money.Create(amount).Value,
                Money.Zero,
                Cashier,
                occurredAt ?? DayStartUtc.AddHours(8),
                [
                    new AccountTransactionDetails(
                        Account(account),
                        TransactionDirection.Credit,
                        Money.Create(amount).Value,
                        account,
                        account
                    ),
                ])
            .Value;

    private static FinancialOperation NewWithdrawal(
        decimal amount,
        DateTimeOffset? occurredAt = null
    ) =>
        FinancialOperation
            .Approve(
                Guid.NewGuid(),
                FinancialOperationKind.Withdrawal,
                Money.Create(amount).Value,
                Money.Create(amount).Value,
                Money.Zero,
                Cashier,
                occurredAt ?? DayStartUtc.AddHours(9),
                [
                    new AccountTransactionDetails(
                        Account("400000002"),
                        TransactionDirection.Debit,
                        Money.Create(amount).Value,
                        "400000002",
                        "400000002"
                    ),
                ])
            .Value;

    private static FinancialOperation NewRejectedWithdrawal(
        decimal amount,
        DateTimeOffset? occurredAt = null
    ) =>
        FinancialOperation
            .Reject(
                Guid.NewGuid(),
                FinancialOperationKind.Withdrawal,
                Money.Create(amount).Value,
                Money.Zero,
                Cashier,
                occurredAt ?? DayStartUtc.AddHours(10),
                "InsufficientFunds",
                [
                    new AccountTransactionDetails(
                        Account("400000002"),
                        TransactionDirection.Debit,
                        Money.Create(amount).Value,
                        "400000002",
                        "400000002"
                    ),
                ])
            .Value;

    private static FinancialOperation NewCardPayment(
        decimal amount,
        int creditCardId,
        DateTimeOffset? occurredAt = null
    ) =>
        FinancialOperation
            .Approve(
                Guid.NewGuid(),
                FinancialOperationKind.CreditCardPayment,
                Money.Create(amount).Value,
                Money.Create(amount).Value,
                Money.Zero,
                Cashier,
                occurredAt ?? DayStartUtc.AddHours(11),
                [
                    new AccountTransactionDetails(
                        Account("400000003"),
                        TransactionDirection.Debit,
                        Money.Create(amount).Value,
                        "400000003",
                        "400000003"
                    ),
                ],
                creditCardId: creditCardId)
            .Value;

    private static FinancialOperation NewLoanPayment(decimal amount) =>
        FinancialOperation
            .Approve(
                Guid.NewGuid(),
                FinancialOperationKind.LoanPayment,
                Money.Create(amount).Value,
                Money.Create(amount).Value,
                Money.Zero,
                Cashier,
                DayStartUtc.AddHours(12),
                [
                    new AccountTransactionDetails(
                        Account("400000004"),
                        TransactionDirection.Debit,
                        Money.Create(amount).Value,
                        "400000004",
                        "400000004"
                    ),
                ],
                loanNumber: LoanNumber.Create("300000001").Value)
            .Value;

    private static FinancialOperation NewTransfer(decimal amount, DateTimeOffset occurredAt) =>
        FinancialOperation
            .Approve(
                Guid.NewGuid(),
                FinancialOperationKind.CashierTransfer,
                Money.Create(amount).Value,
                Money.Create(amount).Value,
                Money.Zero,
                Cashier,
                occurredAt,
                [
                    new AccountTransactionDetails(
                        Account("400000005"),
                        TransactionDirection.Debit,
                        Money.Create(amount).Value,
                        "400000005",
                        "400000006"
                    ),
                    new AccountTransactionDetails(
                        Account("400000006"),
                        TransactionDirection.Credit,
                        Money.Create(amount).Value,
                        "400000005",
                        "400000006"
                    ),
                ])
            .Value;

    private static CreditCard NewCard() =>
        CreditCard
            .Issue(
                "customer-1",
                "1234",
                "abcd".PadRight(64, '0'),
                CvcDigest.Create(new string('c', 64)).Value,
                Money.Create(10_000m).Value,
                "admin",
                DayStartUtc,
                new DateOnly(2026, 8, 6))
            .Value;

    private async Task<IBusinessClock> GetClockAsync() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<IBusinessClock>();
    }

    [Fact]
    public async Task GetDashboardAsync_ReturnsDailyIndicators() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            context.FinancialOperations.AddRange(
                NewDeposit(100m),
                NewDeposit(150m),
                NewWithdrawal(50m),
                NewRejectedWithdrawal(30m),
                NewLoanPayment(900m)
            );
            context.CreditCards.Add(NewCard());
            await context.SaveChangesAsync();

            CreditCard card = await context.CreditCards.SingleAsync();
            context.FinancialOperations.Add(NewCardPayment(700m, card.Id));
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CashierRepository(context, clock);

            var dashboard = await repository.GetDashboardAsync(Cashier, BusinessDate);

            dashboard.TotalTransactions.Should().Be(6);
            dashboard.DepositsToday.Should().Be(2);
            dashboard.WithdrawalsToday.Should().Be(1);
            dashboard.PaymentsToday.Should().Be(1_600m);
        });
    }

    [Fact]
    public async Task GetDashboardAsync_ExcludesOtherDaysAndOtherCashiers() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            context.FinancialOperations.AddRange(
                NewDeposit(100m, occurredAt: DayStartUtc.AddHours(-1)),
                NewDeposit(200m, occurredAt: DayEndUtc),
                NewDeposit(300m, occurredAt: DayEndUtc.AddHours(2))
            );
            // Operación de otro cajero dentro del día de negocio: no debe contar.
            context.FinancialOperations.Add(
                FinancialOperation
                    .Approve(
                        Guid.NewGuid(),
                        FinancialOperationKind.Deposit,
                        Money.Create(999m).Value,
                        Money.Create(999m).Value,
                        Money.Zero,
                        "cashier-other",
                        DayStartUtc.AddHours(6),
                        [
                            new AccountTransactionDetails(
                                Account("400000009"),
                                TransactionDirection.Credit,
                                Money.Create(999m).Value,
                                "400000009",
                                "400000009"
                            ),
                        ])
                    .Value
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CashierRepository(context, clock);

            var dashboard = await repository.GetDashboardAsync(Cashier, BusinessDate);

            dashboard.TotalTransactions.Should().Be(0);
            dashboard.DepositsToday.Should().Be(0);
            dashboard.WithdrawalsToday.Should().Be(0);
            dashboard.PaymentsToday.Should().Be(0m);
        });
    }

    [Fact]
    public async Task GetDashboardAsync_DayBoundary_CountsOperationsInsideRange() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            context.FinancialOperations.AddRange(
                NewDeposit(10m, occurredAt: DayStartUtc),
                NewDeposit(20m, occurredAt: DayEndUtc.AddTicks(-1)),
                NewDeposit(30m, occurredAt: DayEndUtc)
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CashierRepository(context, clock);

            var dashboard = await repository.GetDashboardAsync(Cashier, BusinessDate);

            dashboard.TotalTransactions.Should().Be(2);
            dashboard.DepositsToday.Should().Be(2);
        });
    }

    [Fact]
    public async Task GetOperationsPagedAsync_NewestFirst_WithPagedContract() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            context.FinancialOperations.AddRange(
                NewDeposit(100m, occurredAt: DayStartUtc.AddHours(8)),
                NewDeposit(200m, occurredAt: DayStartUtc.AddHours(9)),
                NewDeposit(300m, occurredAt: DayStartUtc.AddHours(10))
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CashierRepository(context, clock);

            var firstPage = await repository.GetOperationsPagedAsync(
                Cashier,
                new CashierOperationFilters(),
                new PageRequest(page: 1, pageSize: 2)
            );

            firstPage.TotalCount.Should().Be(3);
            firstPage.TotalPages.Should().Be(2);
            firstPage.HasNextPage.Should().BeTrue();
            firstPage.Items.Should().HaveCount(2);
            firstPage.Items[0].Amount.Should().Be(300m);
            firstPage.Items[0].Kind.Should().Be("Deposit");
            firstPage.Items[0].Status.Should().Be("Approved");
            firstPage.Items[0].AccountLastFour.Should().Be("0001");
            firstPage.Items[1].Amount.Should().Be(200m);

            var secondPage = await repository.GetOperationsPagedAsync(
                Cashier,
                new CashierOperationFilters(),
                new PageRequest(page: 2, pageSize: 2)
            );

            secondPage.Items.Should().ContainSingle();
            secondPage.Items[0].Amount.Should().Be(100m);
        });
    }

    [Fact]
    public async Task GetOperationsPagedAsync_FiltersByKindAndDateRange() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            context.FinancialOperations.AddRange(
                NewDeposit(100m, occurredAt: DayStartUtc.AddHours(8)),
                NewWithdrawal(50m, occurredAt: DayStartUtc.AddHours(9)),
                NewRejectedWithdrawal(30m, occurredAt: DayStartUtc.AddHours(10)),
                NewTransfer(1_500m, DayStartUtc.AddHours(13))
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CashierRepository(context, clock);

            var deposits = await repository.GetOperationsPagedAsync(
                Cashier,
                new CashierOperationFilters(Kind: FinancialOperationKind.Deposit),
                new PageRequest()
            );
            deposits.TotalCount.Should().Be(1);
            deposits.Items.Should().ContainSingle(item => item.Kind == "Deposit");

            var withdrawals = await repository.GetOperationsPagedAsync(
                Cashier,
                new CashierOperationFilters(Kind: FinancialOperationKind.Withdrawal),
                new PageRequest()
            );
            withdrawals.TotalCount.Should().Be(2);
            withdrawals
                .Items.Should()
                .ContainSingle(item =>
                    item.Status == "Rejected" && item.RejectionCode == "InsufficientFunds"
                );
            withdrawals.Items.Should().ContainSingle(item => item.Amount == 30m);

            var inRange = await repository.GetOperationsPagedAsync(
                Cashier,
                new CashierOperationFilters(
                    DateFrom: DayStartUtc,
                    DateTo: DayEndUtc.AddTicks(-1)
                ),
                new PageRequest()
            );
            inRange.TotalCount.Should().Be(4);

            var outOfRange = await repository.GetOperationsPagedAsync(
                Cashier,
                new CashierOperationFilters(
                    DateFrom: DayEndUtc,
                    DateTo: DayEndUtc.AddDays(1)
                ),
                new PageRequest()
            );
            outOfRange.TotalCount.Should().Be(0);

            var inclusiveFrom = await repository.GetOperationsPagedAsync(
                Cashier,
                new CashierOperationFilters(DateFrom: DayStartUtc.AddHours(9)),
                new PageRequest()
            );
            inclusiveFrom.TotalCount.Should().Be(3);

            var inclusiveTo = await repository.GetOperationsPagedAsync(
                Cashier,
                new CashierOperationFilters(DateTo: DayStartUtc.AddHours(10)),
                new PageRequest()
            );
            inclusiveTo.TotalCount.Should().Be(3);
        });
    }

    [Fact]
    public async Task GetOperationsPagedAsync_UnknownCashier_ReturnsEmptyPage() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            context.FinancialOperations.Add(NewDeposit(100m));
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CashierRepository(context, clock);

            var result = await repository.GetOperationsPagedAsync(
                "nobody",
                new CashierOperationFilters(),
                new PageRequest()
            );

            result.TotalCount.Should().Be(0);
            result.Items.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task GetOperationsPagedAsync_CardPayment_ShowsOnlyCardLastFour() {
        IBusinessClock clock = await GetClockAsync();

        int cardId = 0;
        await WithContextAsync(async context => {
            var card = NewCard();
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
            cardId = card.Id;

            context.FinancialOperations.Add(NewCardPayment(700m, cardId));
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CashierRepository(context, clock);

            var result = await repository.GetOperationsPagedAsync(
                Cashier,
                new CashierOperationFilters(Kind: FinancialOperationKind.CreditCardPayment),
                new PageRequest()
            );

            result.TotalCount.Should().Be(1);
            result.Items[0].CardLastFour.Should().Be("1234");
            result.Items[0].AccountLastFour.Should().Be("0003");
            result.Items[0].Amount.Should().Be(700m);
        });
    }

    [Fact]
    public async Task GetOperationsPagedAsync_Transfer_ShowsSourceAccount() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            context.FinancialOperations.Add(NewTransfer(1_500m, DayStartUtc.AddHours(14)));
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CashierRepository(context, clock);

            var result = await repository.GetOperationsPagedAsync(
                Cashier,
                new CashierOperationFilters(Kind: FinancialOperationKind.CashierTransfer),
                new PageRequest()
            );

            result.TotalCount.Should().Be(1);
            result.Items[0].Kind.Should().Be("CashierTransfer");
            result.Items[0].Amount.Should().Be(1_500m);
            result.Items[0].AccountLastFour.Should().Be("0005");
        });
    }
}
