using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class SavingsAccountQueryTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset OpenedAt = new(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetPagedAsync_FiltersAndOrdersInSql() {
        await WithContextAsync(async context => {
            SavingsAccount primary = Open(AccountType.Primary, "100000001", "client-1", OpenedAt);
            SavingsAccount secondary = Open(AccountType.Secondary, "100000002", "client-1", OpenedAt.AddHours(1));
            SavingsAccount cancelled = Open(AccountType.Secondary, "100000003", "client-2", OpenedAt.AddHours(2));
            cancelled.Cancel(OpenedAt.AddHours(3)).IsSuccess.Should().BeTrue();
            context.SavingsAccounts.AddRange(primary, secondary, cancelled);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new SavingsAccountRepository(context);

            var all = await repository.GetPagedAsync(null, null, null, new PageRequest());
            var filtered = await repository.GetPagedAsync(
                "client-1",
                AccountStatus.Active,
                AccountType.Secondary,
                new PageRequest()
            );

            all.Items.Select(item => item.Status).Should().Equal("Active", "Active", "Cancelled");
            all.Items.Take(2).Select(item => item.AccountNumber).Should().Equal("100000002", "100000001");
            filtered.Items.Should().ContainSingle();
            filtered.Items[0].AccountNumber.Should().Be("100000002");
        });
    }

    [Fact]
    public async Task GetTransactionsPagedAsync_ReturnsNewestFirstWithContractValues() {
        AccountNumber number = AccountNumber.Create("100000004").Value;
        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(
                SavingsAccount.OpenPrimary("client-1", number, Money.Zero, "admin", OpenedAt).Value
            );
            context.FinancialOperations.AddRange(
                Operation(FinancialOperationKind.Deposit, TransactionDirection.Credit, number, 100m, OpenedAt),
                Operation(FinancialOperationKind.Withdrawal, TransactionDirection.Debit, number, 25m, OpenedAt.AddHours(1))
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new SavingsAccountRepository(context);

            var result = await repository.GetTransactionsPagedAsync(number, new PageRequest());

            result.TotalCount.Should().Be(2);
            result.Items.Select(item => item.TransactionType).Should().Equal("DÉBITO", "CRÉDITO");
            result.Items.Should().OnlyContain(item => item.Status == "APROBADA");
            result.Items[0].Origin.Should().Be(number.Value);
            result.Items[0].Beneficiary.Should().Be("EFECTIVO");
        });
    }

    [Fact]
    public async Task GetTransactionsPagedAsync_AppliesDateAndTypeFiltersInSql() {
        AccountNumber number = AccountNumber.Create("100000005").Value;
        DateTimeOffset depositAt = OpenedAt;
        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(
                SavingsAccount.OpenPrimary("client-1", number, Money.Zero, "admin", OpenedAt).Value
            );
            context.FinancialOperations.AddRange(
                Operation(FinancialOperationKind.Deposit, TransactionDirection.Credit, number, 100m, depositAt),
                Operation(FinancialOperationKind.Withdrawal, TransactionDirection.Debit, number, 25m, depositAt.AddHours(1)),
                Operation(FinancialOperationKind.Deposit, TransactionDirection.Credit, number, 50m, depositAt.AddHours(2))
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new SavingsAccountRepository(context);

            var all = await repository.GetTransactionsPagedAsync(number, new PageRequest());
            all.TotalCount.Should().Be(3);

            var debits = await repository.GetTransactionsPagedAsync(
                number,
                new PageRequest(),
                transactionType: "DÉBITO"
            );
            debits.TotalCount.Should().Be(1);
            debits.Items[0].Amount.Should().Be(25m);

            var credits = await repository.GetTransactionsPagedAsync(
                number,
                new PageRequest(),
                transactionType: "credito"
            );
            credits.TotalCount.Should().Be(2);

            var since = await repository.GetTransactionsPagedAsync(
                number,
                new PageRequest(),
                dateFrom: depositAt.AddHours(1)
            );
            since.TotalCount.Should().Be(2);

            var until = await repository.GetTransactionsPagedAsync(
                number,
                new PageRequest(),
                dateTo: depositAt.AddMinutes(30)
            );
            until.TotalCount.Should().Be(1);

            var combined = await repository.GetTransactionsPagedAsync(
                number,
                new PageRequest(),
                depositAt.AddHours(1),
                depositAt.AddHours(3),
                "CRÉDITO"
            );
            combined.TotalCount.Should().Be(1);
            combined.Items[0].Amount.Should().Be(50m);

            var unknownType = await repository.GetTransactionsPagedAsync(
                number,
                new PageRequest(),
                transactionType: "TRANSFERENCIA"
            );
            unknownType.TotalCount.Should().Be(0);
        });
    }

    private static SavingsAccount Open(
        AccountType type,
        string number,
        string owner,
        DateTimeOffset openedAt
    ) =>
        type == AccountType.Primary
            ? SavingsAccount.OpenPrimary(owner, AccountNumber.Create(number).Value, Money.Zero, "admin", openedAt).Value
            : SavingsAccount.OpenSecondary(owner, AccountNumber.Create(number).Value, Money.Zero, "admin", openedAt).Value;

    private static FinancialOperation Operation(
        FinancialOperationKind kind,
        TransactionDirection direction,
        AccountNumber number,
        decimal amount,
        DateTimeOffset occurredAt
    ) {
        Money money = Money.Create(amount).Value;
        return FinancialOperation.Approve(
            Guid.NewGuid(),
            kind,
            money,
            money,
            Money.Zero,
            "admin",
            occurredAt,
            [new AccountTransactionDetails(number, direction, money, number.Value, "EFECTIVO")]
        ).Value;
    }
}
