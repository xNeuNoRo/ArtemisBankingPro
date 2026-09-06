using ArtemisBankingPro.Application.Features.HermesPay.Handlers;
using ArtemisBankingPro.Application.Features.HermesPay.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
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
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class HermesPayTransactionsQueryTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateOnly IssueDate = new(2026, 8, 11);

    private sealed class FakeCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => "commerce-user-1";
        public string? UserName => "comercio1";
        public string? Role { get; set; } = "Comercio";
        public int? CommerceId { get; set; }
    }

    private static Merchant NewMerchant(
        string name,
        string email,
        string rnc,
        bool active = true
    ) {
        var merchant = Merchant.Create(
            name,
            null,
            email,
            "8095550101",
            rnc,
            "admin",
            IssuedAt.AddHours(-2)
        ).Value;
        if (!active) {
            merchant.Deactivate(IssuedAt.AddHours(-1)).IsSuccess.Should().BeTrue();
        }

        return merchant;
    }

    private async Task<(int MerchantId, int OtherMerchantId, int InactiveMerchantId)> SeedMerchantsAsync() {
        await using (var identityScope = Fixture.Services.CreateAsyncScope()) {
            var roleManager = identityScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roleManager.RoleExistsAsync("Comercio")) {
                (await roleManager.CreateAsync(new IdentityRole("Comercio"))).Succeeded.Should().BeTrue();
            }

            var userManager = identityScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser {
                Id = "commerce-user-1",
                UserName = "comercio1",
                Email = "uno@example.com",
                FirstName = "Comercio",
                LastName = "Uno",
                IdentityDocument = "101000099",
                Active = true,
                CreatedAt = IssuedAt,
            };
            (await userManager.CreateAsync(user, "Strong1!password")).Succeeded.Should().BeTrue();
            (await userManager.AddToRoleAsync(user, "Comercio")).Succeeded.Should().BeTrue();
        }

        int merchantId = 0;
        int otherMerchantId = 0;
        int inactiveMerchantId = 0;
        await WithContextAsync(async context => {
            var merchant = NewMerchant("Comercio Uno", "uno@example.com", "101000099");
            merchant.AssociateUser("commerce-user-1", IssuedAt.AddHours(-1)).IsSuccess.Should().BeTrue();
            var other = NewMerchant("Comercio Dos", "dos@example.com", "101000088");
            var inactive = NewMerchant("Comercio Inactivo", "inactivo@example.com", "101000077", active: false);
            context.Merchants.AddRange(merchant, other, inactive);
            await context.SaveChangesAsync();
            merchantId = merchant.Id;
            otherMerchantId = other.Id;
            inactiveMerchantId = inactive.Id;
        });
        return (merchantId, otherMerchantId, inactiveMerchantId);
    }

    private async Task<(int CardId, int OtherCardId)> SeedCardsAsync() {
        int cardId = 0;
        int otherCardId = 0;
        await WithContextAsync(async context => {
            var card = NewCard("customer-1", "aaaaaa");
            var otherCard = NewCard("customer-2", "bbbbbb");
            context.CreditCards.AddRange(card, otherCard);
            await context.SaveChangesAsync();
            cardId = card.Id;
            otherCardId = otherCard.Id;
        });
        return (cardId, otherCardId);
    }

    private async Task SeedConsumptionsAsync(
        int merchantId,
        int otherMerchantId,
        int cardId,
        int otherCardId
    ) {
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
                NewApprovedHermesPayment(
                    "commerce-user-1",
                    "Comercio Uno",
                    merchantId,
                    otherCardId,
                    account,
                    80m,
                    new DateTimeOffset(2026, 8, 11, 14, 0, 0, TimeSpan.FromHours(-4))),
                NewRejectedHermesPayment(
                    "commerce-user-1",
                    "Comercio Uno",
                    merchantId,
                    cardId,
                    100m,
                    new DateTimeOffset(2026, 8, 11, 13, 0, 0, TimeSpan.FromHours(-4))),
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
    }

    private ServiceProvider BuildProvider(FakeCurrentUser currentUser) =>
        Fixture.BuildProvider(configure: services => {
            services.AddScoped<ICurrentUserService>(_ => currentUser);
        });

    private static GetCommerceTransactionsQueryHandler CreateHandler(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IMerchantRepository>(),
            provider.GetRequiredService<ICreditCardRepository>(),
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<ICurrentUserService>()
        );

    [Fact]
    public async Task GetTransactions_CommerceRole_IgnoresRouteParamAndUsesJwtCommerce() {
        (int merchantId, int otherMerchantId, _) = await SeedMerchantsAsync();
        (int cardId, int otherCardId) = await SeedCardsAsync();
        await SeedConsumptionsAsync(merchantId, otherMerchantId, cardId, otherCardId);

        var currentUser = new FakeCurrentUser { Role = "Comercio", CommerceId = merchantId };
        await using var provider = BuildProvider(currentUser);
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new GetCommerceTransactionsQuery(
                CommerceId: otherMerchantId,
                Page: 1,
                PageSize: 2
            ),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CommerceId.Should().Be(merchantId);
        result.Value.CommerceName.Should().Be("Comercio Uno");
        result.Value.TotalRecords.Should().Be(3);
        result.Value.TotalPages.Should().Be(2);
        result.Value.Data.Should().HaveCount(2);
        result.Value.Data[0].Amount.Should().Be(250m);
        result.Value.Data[0].CardLastFourDigits.Should().Be("1234");
        result.Value.Data[0].Status.Should().Be("APROBADO");
        result.Value.Data[1].Amount.Should().Be(80m);
        result.Value.Data[0].TransactionDate.Should().BeAfter(result.Value.Data[1].TransactionDate);
    }

    [Fact]
    public async Task GetTransactions_Administrator_UsesRouteCommerceId() {
        (int merchantId, int otherMerchantId, _) = await SeedMerchantsAsync();
        (int cardId, int otherCardId) = await SeedCardsAsync();
        await SeedConsumptionsAsync(merchantId, otherMerchantId, cardId, otherCardId);

        var currentUser = new FakeCurrentUser { Role = "Administrador", CommerceId = null };
        await using var provider = BuildProvider(currentUser);
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new GetCommerceTransactionsQuery(CommerceId: otherMerchantId),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CommerceId.Should().Be(otherMerchantId);
        result.Value.CommerceName.Should().Be("Comercio Dos");
        result.Value.TotalRecords.Should().Be(1);
        result.Value.Data.Should().ContainSingle();
        result.Value.Data[0].Amount.Should().Be(999m);
        result.Value.Data[0].Status.Should().Be("APROBADO");
    }

    [Fact]
    public async Task GetTransactions_UnknownMerchant_ReturnsNotFound() {
        var currentUser = new FakeCurrentUser { Role = "Administrador", CommerceId = null };
        await using var provider = BuildProvider(currentUser);
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new GetCommerceTransactionsQuery(CommerceId: int.MaxValue),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.NotFound");
    }

    [Fact]
    public async Task GetTransactions_InactiveMerchant_ReturnsValidationError() {
        (_, _, int inactiveMerchantId) = await SeedMerchantsAsync();

        var currentUser = new FakeCurrentUser { Role = "Administrador", CommerceId = null };
        await using var provider = BuildProvider(currentUser);
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new GetCommerceTransactionsQuery(CommerceId: inactiveMerchantId),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.Inactive");
    }

    [Fact]
    public async Task GetTransactions_PaginationContract_SecondPageKeepsTotals() {
        (int merchantId, int otherMerchantId, _) = await SeedMerchantsAsync();
        (int cardId, int otherCardId) = await SeedCardsAsync();
        await SeedConsumptionsAsync(merchantId, otherMerchantId, cardId, otherCardId);

        var currentUser = new FakeCurrentUser { Role = "Administrador", CommerceId = null };
        await using var provider = BuildProvider(currentUser);
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new GetCommerceTransactionsQuery(CommerceId: merchantId, Page: 2, PageSize: 2),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(2);
        result.Value.PageSize.Should().Be(2);
        result.Value.TotalRecords.Should().Be(3);
        result.Value.TotalPages.Should().Be(2);
        result.Value.Data.Should().ContainSingle();
        result.Value.Data[0].Status.Should().Be("RECHAZADO");
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
