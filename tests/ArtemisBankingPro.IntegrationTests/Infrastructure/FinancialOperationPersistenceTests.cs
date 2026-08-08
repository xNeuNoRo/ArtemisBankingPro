using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class FinancialOperationPersistenceTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4));

    private static FinancialOperation NewApprovedTransfer(Guid? id = null) {
        AccountNumber source = AccountNumber.Create("400000001").Value;
        AccountNumber destination = AccountNumber.Create("400000002").Value;
        Money amount = Money.Create(100m).Value;

        return FinancialOperation.Approve(
            id ?? Guid.NewGuid(),
            FinancialOperationKind.ExpressTransfer,
            amount,
            amount,
            Money.Zero,
            "client-1",
            OccurredAt,
            [
                new AccountTransactionDetails(source, TransactionDirection.Debit, amount, source.Value, destination.Value),
                new AccountTransactionDetails(destination, TransactionDirection.Credit, amount, source.Value, destination.Value),
            ]).Value;
    }

    [Fact]
    public async Task Approve_RoundTrip_WithPairedTransactions() {
        FinancialOperation operation = NewApprovedTransfer();

        await WithContextAsync(async context => {
            context.FinancialOperations.Add(operation);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new FinancialOperationRepository(context);

            FinancialOperation? loaded = await repository.GetByIdAsync(operation.Id);
            Assert.NotNull(loaded);
            loaded.Kind.Should().Be(FinancialOperationKind.ExpressTransfer);
            loaded.Status.Should().Be(FinancialOperationStatus.Approved);
            loaded.AppliedAmount.Amount.Should().Be(100m);
            loaded.AccountTransactions.Should().HaveCount(2);
            loaded.AccountTransactions.Should().Contain(transaction =>
                transaction.Direction == TransactionDirection.Debit
                && transaction.Amount.Amount == 100m
            );
            loaded.AccountTransactions.Should().Contain(transaction =>
                transaction.Direction == TransactionDirection.Credit
                && transaction.Amount.Amount == 100m
            );
        });
    }

    [Fact]
    public async Task Reject_RoundTrip_WithRejectionCodeAndZeroApplied() {
        Guid id = Guid.NewGuid();
        FinancialOperation operation = FinancialOperation.Reject(
            id,
            FinancialOperationKind.Withdrawal,
            Money.Create(50m).Value,
            Money.Zero,
            "cashier-1",
            OccurredAt,
            "InsufficientFunds",
            [
                new AccountTransactionDetails(
                    AccountNumber.Create("400000001").Value,
                    TransactionDirection.Debit,
                    Money.Create(50m).Value,
                    "400000001",
                    "400000001"
                ),
            ]).Value;

        await WithContextAsync(async context => {
            context.FinancialOperations.Add(operation);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new FinancialOperationRepository(context);

            FinancialOperation? loaded = await repository.GetByIdAsync(id);
            Assert.NotNull(loaded);
            loaded.Status.Should().Be(FinancialOperationStatus.Rejected);
            loaded.AppliedAmount.Should().Be(Money.Zero);
            loaded.RejectionCode.Should().Be("InsufficientFunds");
            loaded.AccountTransactions.Should().HaveCount(1);
        });
    }

    [Fact]
    public async Task GetByAccountNumber_ReturnsPagedHistory_NewestFirst() {
        AccountNumber account = AccountNumber.Create("400000001").Value;

        await WithContextAsync(async context => {
            for (int index = 0; index < 5; index++) {
                context.FinancialOperations.Add(NewApprovedTransfer());
            }
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new FinancialOperationRepository(context);
            var page = new PageRequest(page: 1, pageSize: 2);

            PageResult<FinancialOperation> result = await repository.GetByAccountNumberAsync(
                account,
                page
            );

            result.Items.Should().HaveCount(2);
            result.TotalCount.Should().Be(5);
            result.TotalPages.Should().Be(3);
            result.HasNextPage.Should().BeTrue();
        });
    }

    [Fact]
    public async Task GetByInitiator_ReturnsOnlyInitiatorOperations() {
        Guid id = Guid.NewGuid();
        FinancialOperation operation = NewApprovedTransfer(id);

        await WithContextAsync(async context => {
            context.FinancialOperations.Add(operation);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new FinancialOperationRepository(context);
            var page = new PageRequest();

            PageResult<FinancialOperation> result = await repository.GetByInitiatorAsync(
                "client-1",
                page
            );
            result.TotalCount.Should().Be(1);

            PageResult<FinancialOperation> none = await repository.GetByInitiatorAsync(
                "client-2",
                page
            );
            none.TotalCount.Should().Be(0);
        });
    }

    [Fact]
    public async Task HermesPayment_RoundTrip_WithCardConsumption() {
        Guid id = Guid.NewGuid();
        AccountNumber commerceAccount = AccountNumber.Create("400000003").Value;
        Money amount = Money.Create(250m).Value;

        int cardId = 0;
        int merchantId = 0;
        await WithContextAsync(async context => {
            var card = CreditCard.Issue(
                "customer-1",
                "1234",
                new string('e', 64),
                CvcDigest.Create(new string('f', 64)).Value,
                Money.Create(10_000m).Value,
                "admin",
                OccurredAt,
                DateOnly.FromDateTime(OccurredAt.DateTime)).Value;
            var merchant = Merchant.Create(
                "Comercio Uno",
                null,
                "hermes@example.com",
                "8095550101",
                "101000099",
                "admin",
                OccurredAt).Value;
            context.CreditCards.Add(card);
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
            cardId = card.Id;
            merchantId = merchant.Id;
        });

        FinancialOperation operation = FinancialOperation.Approve(
            id,
            FinancialOperationKind.HermesPayment,
            amount,
            amount,
            Money.Zero,
            "commerce-user-1",
            OccurredAt,
            [
                new AccountTransactionDetails(
                    commerceAccount,
                    TransactionDirection.Credit,
                    amount,
                    "900000000000001X",
                    commerceAccount.Value
                ),
            ],
            new CardConsumptionDetails(
                CreditCardId: cardId,
                MerchantId: merchantId,
                MerchantDisplayName: "Comercio Uno",
                Type: ConsumptionType.Purchase,
                Amount: amount
            ),
            creditCardId: cardId,
            merchantId: merchantId
        ).Value;

        await WithContextAsync(async context => {
            context.FinancialOperations.Add(operation);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new FinancialOperationRepository(context);

            FinancialOperation? loaded = await repository.GetByIdAsync(id);
            Assert.NotNull(loaded);
            Assert.NotNull(loaded.CardConsumption);
            loaded.CardConsumption.CreditCardId.Should().Be(cardId);
            loaded.CardConsumption.MerchantId.Should().Be(merchantId);
            loaded.CardConsumption.Type.Should().Be(ConsumptionType.Purchase);
            loaded.CardConsumption.Amount.Amount.Should().Be(250m);
        });
    }
}
