using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class CreditCardConsumptionQueryTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateOnly IssueDate = new(2026, 8, 6);
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4));

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
            new DateTimeOffset(2026, 8, 6, 11, 0, 0, TimeSpan.FromHours(-4))).Value;

    [Fact]
    public async Task GetConsumptionsPagedAsync_ReturnsNewestFirst_WithAvanceForCashAdvances() {
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
        });

        AccountNumber account = AccountNumber.Create("400000001").Value;

        await WithContextAsync(async context => {
            context.FinancialOperations.Add(NewApprovedHermesPayment(
                "commerce-user-1",
                "Comercio Uno",
                merchantId,
                cardId,
                account,
                250m,
                new DateTimeOffset(2026, 8, 6, 14, 0, 0, TimeSpan.FromHours(-4))));
            context.FinancialOperations.Add(NewApprovedCashAdvance(
                "customer-1",
                cardId,
                account,
                500m,
                25m,
                new DateTimeOffset(2026, 8, 6, 13, 0, 0, TimeSpan.FromHours(-4))));
            context.FinancialOperations.Add(NewRejectedHermesPayment(
                "commerce-user-1",
                "Comercio Dos",
                merchantId,
                cardId,
                100m,
                new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4))));
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);

            CreditCard? loaded = await repository.GetByIdAsync(cardId);
            Assert.NotNull(loaded);
            loaded.LastFour.Should().Be("1234");

            PageResult<CardConsumptionView> firstPage = await repository.GetConsumptionsPagedAsync(
                cardId,
                new PageRequest(page: 1, pageSize: 2)
            );

            firstPage.TotalCount.Should().Be(3);
            firstPage.Items.Should().HaveCount(2);
            firstPage.Items[0].CommerceName.Should().Be("Comercio Uno");
            firstPage.Items[0].Amount.Should().Be(250m);
            firstPage.Items[0].Status.Should().Be(FinancialOperationStatus.Approved);
            firstPage.Items[1].CommerceName.Should().Be("AVANCE");
            firstPage.Items[1].Amount.Should().Be(525m);
            firstPage.Items[1].Status.Should().Be(FinancialOperationStatus.Approved);
            firstPage.Items[0].Date.Should().BeAfter(firstPage.Items[1].Date);
            firstPage.Items[0].Id.Should().NotBe(firstPage.Items[1].Id);

            PageResult<CardConsumptionView> secondPage = await repository.GetConsumptionsPagedAsync(
                cardId,
                new PageRequest(page: 2, pageSize: 2)
            );

            secondPage.Items.Should().HaveCount(1);
            secondPage.Items[0].CommerceName.Should().Be("Comercio Dos");
            secondPage.Items[0].Amount.Should().Be(100m);
            secondPage.Items[0].Status.Should().Be(FinancialOperationStatus.Rejected);
            secondPage.Items[0].Date.Should().BeBefore(firstPage.Items[1].Date);
        });
    }

    [Fact]
    public async Task GetConsumptionsPagedAsync_CardWithoutConsumptions_ReturnsEmptyPage() {
        int cardId = 0;
        await WithContextAsync(async context => {
            var card = NewCard();
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
            cardId = card.Id;
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);

            PageResult<CardConsumptionView> result = await repository.GetConsumptionsPagedAsync(
                cardId,
                new PageRequest()
            );

            result.TotalCount.Should().Be(0);
            result.Items.Should().BeEmpty();
            result.TotalPages.Should().Be(0);
        });
    }

    [Fact]
    public async Task GetConsumptionsPagedAsync_PageBeyondData_ReturnsEmptyItemsAndKeepsTotal() {
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
        });

        AccountNumber account = AccountNumber.Create("400000001").Value;

        await WithContextAsync(async context => {
            context.FinancialOperations.Add(NewApprovedHermesPayment(
                "commerce-user-1",
                "Comercio Uno",
                merchantId,
                cardId,
                account,
                250m,
                new DateTimeOffset(2026, 8, 6, 14, 0, 0, TimeSpan.FromHours(-4))));
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);

            PageResult<CardConsumptionView> result = await repository.GetConsumptionsPagedAsync(
                cardId,
                new PageRequest(page: 5, pageSize: 2)
            );

            result.Items.Should().BeEmpty();
            result.TotalCount.Should().Be(1);
            result.Page.Should().Be(5);
            result.PageSize.Should().Be(2);
        });
    }

    [Fact]
    public async Task GetConsumptionsPagedAsync_IgnoresConsumptionsOfOtherCards() {
        int cardId = 0;
        int otherCardId = 0;
        int merchantId = 0;

        await WithContextAsync(async context => {
            var card = NewCard("customer-1", "aaaaaa");
            var otherCard = NewCard("customer-2", "bbbbbb");
            var merchant = NewMerchant();
            context.CreditCards.AddRange(card, otherCard);
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
            cardId = card.Id;
            otherCardId = otherCard.Id;
            merchantId = merchant.Id;
        });

        AccountNumber account = AccountNumber.Create("400000001").Value;

        await WithContextAsync(async context => {
            context.FinancialOperations.Add(NewApprovedHermesPayment(
                "commerce-user-1",
                "Comercio Uno",
                merchantId,
                cardId,
                account,
                250m,
                new DateTimeOffset(2026, 8, 6, 14, 0, 0, TimeSpan.FromHours(-4))));
            context.FinancialOperations.Add(NewApprovedHermesPayment(
                "commerce-user-2",
                "Comercio Uno",
                merchantId,
                otherCardId,
                account,
                80m,
                new DateTimeOffset(2026, 8, 6, 14, 30, 0, TimeSpan.FromHours(-4))));
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);

            PageResult<CardConsumptionView> result = await repository.GetConsumptionsPagedAsync(
                cardId,
                new PageRequest()
            );

            result.TotalCount.Should().Be(1);
            result.Items.Should().ContainSingle();
            result.Items[0].Amount.Should().Be(250m);
            result.Items[0].CommerceName.Should().Be("Comercio Uno");
        });
    }

    private static FinancialOperation NewApprovedHermesPayment(
        string initiatedBy,
        string merchantName,
        int merchantId,
        int cardId,
        AccountNumber account,
        decimal amount,
        DateTimeOffset occurredAt
    ) =>
        FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.HermesPayment,
            Money.Create(amount).Value,
            Money.Create(amount).Value,
            Money.Zero,
            initiatedBy,
            occurredAt,
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

    private static FinancialOperation NewApprovedCashAdvance(
        string initiatedBy,
        int cardId,
        AccountNumber account,
        decimal principal,
        decimal interest,
        DateTimeOffset occurredAt
    ) =>
        FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.CashAdvance,
            Money.Create(principal).Value,
            Money.Create(principal).Value,
            Money.Create(interest).Value,
            initiatedBy,
            occurredAt,
            [
                new AccountTransactionDetails(
                    account,
                    TransactionDirection.Credit,
                    Money.Create(principal).Value,
                    account.Value,
                    account.Value
                ),
            ],
            new CardConsumptionDetails(
                cardId,
                null,
                "AVANCE",
                ConsumptionType.CashAdvance,
                Money.Create(principal + interest).Value
            ),
            creditCardId: cardId).Value;

    private static FinancialOperation NewRejectedHermesPayment(
        string initiatedBy,
        string merchantName,
        int merchantId,
        int cardId,
        decimal amount,
        DateTimeOffset occurredAt
    ) =>
        FinancialOperation.Reject(
            Guid.NewGuid(),
            FinancialOperationKind.HermesPayment,
            Money.Create(amount).Value,
            Money.Zero,
            initiatedBy,
            occurredAt,
            "InsufficientLimit",
            [],
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
