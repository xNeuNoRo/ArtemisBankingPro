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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class CancelCreditCardTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateOnly IssueDate = new(2026, 8, 10);
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(-4));

    private static CreditCard NewCard(string customer = "customer-1", string fingerprint = "aaaaaa") =>
        CreditCard.Issue(
            customer,
            "1234",
            fingerprint.PadRight(64, '0'),
            CvcDigest.Create(new string('c', 64)).Value,
            Money.Create(10_000m).Value,
            "admin",
            IssuedAt,
            IssueDate).Value;

    private static Merchant NewMerchant() =>
        Merchant.Create(
            "Comercio Uno",
            null,
            "hermes@example.com",
            "8095550101",
            "101000099",
            "admin",
            new DateTimeOffset(2026, 8, 10, 11, 0, 0, TimeSpan.FromHours(-4))).Value;

    [Fact]
    public async Task Cancel_CancelsCardAndPreservesConsumptionHistory() {
        int cardId = 0;
        int merchantId = 0;

        await WithContextAsync(async context => {
            var card = NewCard();
            var merchant = NewMerchant();
            context.CreditCards.Add(card);
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
            cardId = card.Id;
            merchantId = merchant.Id;

            AccountNumber account = AccountNumber.Create("400000001").Value;
            context.FinancialOperations.Add(NewApprovedHermesPayment(
                "commerce-user-1",
                "Comercio Uno",
                merchantId,
                cardId,
                account,
                250m));
            await context.SaveChangesAsync();
        });

        DateTimeOffset cancelledAt = IssuedAt.AddDays(1);
        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);
            CreditCard? card = await repository.GetByIdAsync(cardId);
            Assert.NotNull(card);

            card.Cancel(cancelledAt).IsSuccess.Should().BeTrue();
            repository.Update(card);

            context.FinancialOperations.Add(
                FinancialOperation.Approve(
                    Guid.NewGuid(),
                    FinancialOperationKind.CardCancelled,
                    Money.Zero,
                    Money.Zero,
                    Money.Zero,
                    "admin",
                    cancelledAt,
                    [],
                    creditCardId: cardId).Value
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            CreditCard? cancelled = await context.CreditCards
                .SingleOrDefaultAsync(card => card.Id == cardId);
            Assert.NotNull(cancelled);
            cancelled.Status.Should().Be(CreditCardStatus.Cancelled);
            cancelled.CancelledAt.Should().Be(cancelledAt);

            int consumptionCount = await context.CardConsumptions
                .CountAsync(consumption => consumption.CreditCardId == cardId);
            consumptionCount.Should().Be(1);

            FinancialOperation operation = await context.FinancialOperations
                .SingleAsync(operation => operation.Kind == FinancialOperationKind.CardCancelled);
            operation.CreditCardId.Should().Be(cardId);
            operation.RequestedAmount.Should().Be(Money.Zero);
            operation.AppliedAmount.Should().Be(Money.Zero);
            operation.AccountTransactions.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Cancel_CardWithDebt_IsRejectedWithoutHistoryChanges() {
        int cardId = 0;
        await WithContextAsync(async context => {
            var card = NewCard();
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
            cardId = card.Id;
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);
            CreditCard? card = await repository.GetByIdAsync(cardId);
            Assert.NotNull(card);
            card.AuthorizeCharge(Money.Create(500m).Value, IssueDate).IsSuccess.Should().BeTrue();
            repository.Update(card);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);
            CreditCard? card = await repository.GetByIdAsync(cardId);
            Assert.NotNull(card);

            Result cancelResult = card.Cancel(IssuedAt.AddDays(1));

            cancelResult.IsFailure.Should().BeTrue();
            cancelResult.Error!.Code.Should().Be("Card.DebtMustBeZero");
        });

        await WithContextAsync(async context => {
            CreditCard? reloaded = await context.CreditCards
                .SingleOrDefaultAsync(card => card.Id == cardId);
            Assert.NotNull(reloaded);
            reloaded.Status.Should().Be(CreditCardStatus.Active);
            reloaded.CancelledAt.Should().BeNull();

            (await context.FinancialOperations
                .CountAsync(operation => operation.Kind == FinancialOperationKind.CardCancelled))
                .Should().Be(0);
        });
    }

    [Fact]
    public async Task FinancialOperations_ModelConstraint_AllowsZeroAmountOnlyForCardCancelled() {
        await WithContextAsync(async context => {
            var result = FinancialOperation.Approve(
                Guid.NewGuid(),
                FinancialOperationKind.CardCancelled,
                Money.Zero,
                Money.Zero,
                Money.Zero,
                "admin",
                IssuedAt,
                [],
                creditCardId: 1
            );
            result.IsSuccess.Should().BeTrue();
            context.FinancialOperations.Add(result.Value);
            await context.SaveChangesAsync();

            context.GetService<IDesignTimeModel>().Model
                .FindEntityType(typeof(FinancialOperation))!
                .GetCheckConstraints()
                .Should()
                .Contain(constraint =>
                    constraint.Name == "CK_FinancialOperations_Requested_Positive"
                    && constraint.Sql.Contains("[RequestedAmount] > 0", StringComparison.Ordinal)
                );
        });
    }

    private static FinancialOperation NewApprovedHermesPayment(
        string initiatedBy,
        string merchantName,
        int merchantId,
        int cardId,
        AccountNumber account,
        decimal amount
    ) =>
        FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.HermesPayment,
            Money.Create(amount).Value,
            Money.Create(amount).Value,
            Money.Zero,
            initiatedBy,
            IssuedAt.AddHours(2),
            [
                new AccountTransactionDetails(
                    account,
                    TransactionDirection.Credit,
                    Money.Create(amount).Value,
                    "900000000000001X",
                    account.Value
                ),
            ],
            new CardConsumptionDetails(
                cardId,
                merchantId,
                merchantName,
                ConsumptionType.Purchase,
                Money.Create(amount).Value
            ),
            creditCardId: cardId,
            merchantId: merchantId).Value;
}
