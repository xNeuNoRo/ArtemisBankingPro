using ArtemisBankingPro.Application.Features.HermesPay.DTOs;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
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
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class HermesPayRepositorySupportTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string CommerceUserId = "hermes-user-1";
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateOnly IssueDate = new(2026, 8, 11);

    private static Merchant NewMerchant(string rnc = "101000099", string email = "hermes@example.com") =>
        Merchant.Create(
            "Comercio Uno",
            null,
            email,
            "8095550101",
            rnc,
            "admin",
            IssuedAt.AddHours(-2)).Value;

    private static async Task<Merchant> PersistMerchantAsync(
        BankingDbContext context,
        string? associatedUserId = null,
        string rnc = "101000099",
        string email = "hermes@example.com"
    ) {
        var merchant = NewMerchant(rnc, email);
        if (associatedUserId is not null) {
            merchant.AssociateUser(associatedUserId, IssuedAt.AddHours(-1))
                .IsSuccess
                .Should()
                .BeTrue();
        }
        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();
        return merchant;
    }

    private static async Task<SavingsAccount> PersistPrincipalAccountAsync(
        BankingDbContext context,
        string ownerUserId,
        string rawNumber = "500000001"
    ) {
        var account = SavingsAccount.OpenPrimary(
            ownerUserId,
            AccountNumber.Create(rawNumber).Value,
            Money.Create(1000m).Value,
            "admin",
            IssuedAt
        ).Value;
        context.SavingsAccounts.Add(account);
        await context.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task GetPrincipalByCommerceIdAsync_AssociatedUser_ReturnsActivePrincipal() {
        int merchantId = 0;
        await WithContextAsync(async context => {
            var merchant = await PersistMerchantAsync(context, CommerceUserId);
            merchantId = merchant.Id;
            await PersistPrincipalAccountAsync(context, CommerceUserId);
        });

        await using var scope = Fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();

        var account = await repository.GetPrincipalByCommerceIdAsync(merchantId);

        Assert.NotNull(account);
        account.OwnerUserId.Should().Be(CommerceUserId);
        account.Type.Should().Be(AccountType.Primary);
        account.Status.Should().Be(AccountStatus.Active);
        account.Balance.Amount.Should().Be(1000m);
    }

    [Fact]
    public async Task GetPrincipalByCommerceIdAsync_UnknownMerchant_ReturnsNull() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();

        var account = await repository.GetPrincipalByCommerceIdAsync(int.MaxValue);

        Assert.Null(account);
    }

    [Fact]
    public async Task GetPrincipalByCommerceIdAsync_MerchantWithoutUser_ReturnsNull() {
        int merchantId = 0;
        await WithContextAsync(async context => {
            var merchant = await PersistMerchantAsync(context);
            merchantId = merchant.Id;
            await PersistPrincipalAccountAsync(context, "other-user");
        });

        await using var scope = Fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();

        var account = await repository.GetPrincipalByCommerceIdAsync(merchantId);

        Assert.Null(account);
    }

    [Fact]
    public async Task GetPrincipalByCommerceIdAsync_UserWithoutAccount_ReturnsNull() {
        int merchantId = 0;
        await WithContextAsync(async context => {
            var merchant = await PersistMerchantAsync(context, CommerceUserId);
            merchantId = merchant.Id;
        });

        await using var scope = Fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();

        var account = await repository.GetPrincipalByCommerceIdAsync(merchantId);

        Assert.Null(account);
    }

    [Fact]
    public async Task GetConsumptionsByMerchantPagedAsync_ReturnsNewestFirst_WithCardLastFourAndStatus() {
        int cardId = 0;
        int otherCardId = 0;
        int merchantId = 0;
        int otherMerchantId = 0;

        await WithContextAsync(async context => {
            var card = NewCard("customer-1", "aaaaaa");
            var otherCard = NewCard("customer-2", "bbbbbb");
            var merchant = await PersistMerchantAsync(context);
            var otherMerchant = await PersistMerchantAsync(context, rnc: "101000088", email: "otro@example.com");
            context.CreditCards.AddRange(card, otherCard);
            await context.SaveChangesAsync();
            cardId = card.Id;
            otherCardId = otherCard.Id;
            merchantId = merchant.Id;
            otherMerchantId = otherMerchant.Id;
        });

        AccountNumber account = AccountNumber.Create("400000001").Value;

        await WithContextAsync(async context => {
            context.FinancialOperations.AddRange(
                NewApprovedHermesPayment(
                    "commerce-user-1",
                    "Comercio Uno",
                    merchantId,
                    cardId,
                    account,
                    250m,
                    new DateTimeOffset(2026, 8, 11, 15, 0, 0, TimeSpan.FromHours(-4))),
                NewRejectedHermesPayment(
                    "commerce-user-1",
                    "Comercio Uno",
                    merchantId,
                    cardId,
                    100m,
                    new DateTimeOffset(2026, 8, 11, 13, 0, 0, TimeSpan.FromHours(-4))),
                NewApprovedHermesPayment(
                    "commerce-user-1",
                    "Comercio Uno",
                    merchantId,
                    otherCardId,
                    account,
                    80m,
                    new DateTimeOffset(2026, 8, 11, 14, 0, 0, TimeSpan.FromHours(-4))),
                NewApprovedHermesPayment(
                    "commerce-user-2",
                    "Comercio Dos",
                    otherMerchantId,
                    cardId,
                    account,
                    999m,
                    new DateTimeOffset(2026, 8, 11, 16, 0, 0, TimeSpan.FromHours(-4)))
            );
            await context.SaveChangesAsync();
        });

        await using var scope = Fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICreditCardRepository>();

        PageResult<CommerceTransactionDto> result =
            await repository.GetConsumptionsByMerchantPagedAsync(
                merchantId,
                new PageRequest(page: 1, pageSize: 2)
            );

        result.TotalCount.Should().Be(3);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
        result.TotalPages.Should().Be(2);
        result.Items.Should().HaveCount(2);

        result.Items[0].Amount.Should().Be(250m);
        result.Items[0].CardLastFourDigits.Should().Be("1234");
        result.Items[0].Status.Should().Be("APROBADO");
        result.Items[0].TransactionDate.Should().BeAfter(result.Items[1].TransactionDate);

        result.Items[1].Amount.Should().Be(80m);
        result.Items[1].CardLastFourDigits.Should().Be("1234");
        result.Items[1].Status.Should().Be("APROBADO");

        PageResult<CommerceTransactionDto> secondPage =
            await repository.GetConsumptionsByMerchantPagedAsync(
                merchantId,
                new PageRequest(page: 2, pageSize: 2)
            );

        secondPage.Items.Should().ContainSingle();
        secondPage.Items[0].Amount.Should().Be(100m);
        secondPage.Items[0].Status.Should().Be("RECHAZADO");
        secondPage.Items[0].Id.Should().NotBe(result.Items[0].Id);
    }

    [Fact]
    public async Task GetConsumptionsByMerchantPagedAsync_UnknownMerchant_ReturnsEmptyPage() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICreditCardRepository>();

        PageResult<CommerceTransactionDto> result =
            await repository.GetConsumptionsByMerchantPagedAsync(int.MaxValue, new PageRequest());

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
        result.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task GetConsumptionsByMerchantPagedAsync_PageBeyondData_KeepsTotal() {
        int cardId = 0;
        int merchantId = 0;

        await WithContextAsync(async context => {
            var card = NewCard();
            var merchant = await PersistMerchantAsync(context);
            context.CreditCards.Add(card);
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
                new DateTimeOffset(2026, 8, 11, 15, 0, 0, TimeSpan.FromHours(-4))));
            await context.SaveChangesAsync();
        });

        await using var scope = Fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICreditCardRepository>();

        PageResult<CommerceTransactionDto> result =
            await repository.GetConsumptionsByMerchantPagedAsync(
                merchantId,
                new PageRequest(page: 5, pageSize: 2)
            );

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(1);
        result.Page.Should().Be(5);
        result.PageSize.Should().Be(2);
    }

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
