using ArtemisBankingPro.Application.Features.HermesPay.Commands;
using ArtemisBankingPro.Application.Features.HermesPay.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.Security;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

/// <summary>
/// Flujo financiero completo de Hermes Pay sobre SQL Server real (spec §41):
/// pago aprobado (deuda + consumo + crédito al comercio + operación), rechazo
/// por crédito insuficiente persistido en su propia transacción, y doble pago
/// concurrente resuelto por rowversion (un único cobro).
/// </summary>
[Collection("SqlServer")]
public sealed class HermesPayIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string CommerceUserName = "hermescomercio";
    private const string CardOwnerUserName = "hermescliente";
    private const string MerchantEmail = "hermes-comercio@example.com";
    private const string Pan = "1589963258467598";
    private const string CardLastFour = "7598";
    private const string Cvc = "859";
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateOnly IssueDate = new(2026, 8, 11);

    private int _documentCounter;

    private sealed class MutableCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? Role { get; set; } = nameof(Roles.Comercio);
        public int? CommerceId { get; set; }
    }

    private sealed class NoopEmailService : IEmailService {
        public Task SendAsync<T>(string recipient, T model, CancellationToken ct = default)
            where T : IEmailModel => Task.CompletedTask;
    }

    private async Task<AppUser> CreateUserAsync(string userName, string role) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        string document = $"81000{(_documentCounter++):D5}";
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Hermes",
            LastName = "Prueba",
            IdentityDocument = document,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();

        return user;
    }

    private async Task<SavingsAccount> AddPrincipalAccountAsync(
        string ownerUserId,
        decimal initialBalance
    ) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var numberGenerator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
        var accountRepository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string rawNumber = await numberGenerator.NextAccountNumberAsync();
        var account = SavingsAccount.OpenPrimary(
            ownerUserId,
            AccountNumber.Create(rawNumber).Value,
            Money.Create(initialBalance).Value,
            "admin",
            IssuedAt
        ).Value;

        await unitOfWork.ExecuteInTransactionAsync(async ct => {
            await accountRepository.AddAsync(account, ct);
            return Result.Success();
        });

        return account;
    }

    private async Task<Merchant> CreateMerchantAsync(string associatedUserId) {
        var merchant = Merchant.Create(
            "Comercio Hermes",
            null,
            MerchantEmail,
            "8095550101",
            "101000099",
            "admin",
            IssuedAt.AddHours(-2)
        ).Value;
        merchant.AssociateUser(associatedUserId, IssuedAt.AddHours(-1))
            .IsSuccess
            .Should()
            .BeTrue();

        await WithContextAsync(async context => {
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
        });

        return merchant;
    }

    private async Task<int> SeedCardAsync(string customerUserId, decimal limit) {
        // El proveedor base del fixture no configura las claves de tarjeta;
        // el seeding usa el mismo provider con claves que el flujo bajo prueba.
        await using var provider = BuildHermesProvider(new MutableCurrentUser());
        await using var scope = provider.CreateAsyncScope();
        var securityService = scope.ServiceProvider.GetRequiredService<ICardSecurityService>();
        string fingerprint = securityService.ComputePanFingerprint(Pan);
        string cvcDigest = securityService.ComputeCvcDigest(Cvc);
        int cardId = 0;

        await WithContextAsync(async context => {
            var card = CreditCard.Issue(
                customerUserId,
                CardLastFour,
                fingerprint,
                CvcDigest.Create(cvcDigest).Value,
                Money.Create(limit).Value,
                "admin",
                IssuedAt,
                IssueDate
            ).Value;
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
            cardId = card.Id;
        });

        return cardId;
    }

    private ServiceProvider BuildHermesProvider(MutableCurrentUser currentUser) =>
        BuildProvider(
            configure: services => {
                services.AddScoped<ICurrentUserService>(_ => currentUser);
                services.AddScoped<IEmailService>(_ => new NoopEmailService());
            },
            extraConfiguration: new Dictionary<string, string?> {
                ["Security:Card:FingerprintKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
                ["Security:Card:CvcPepperKey"] = "ZmUwMTIzNDU2Nzg5YWJjZGVmMDEyMzQ1Njc4OWFiY2Rl",
            }
        );

    private static ProcessHermesPayCommandHandler CreatePaymentHandler(
        IServiceProvider provider
    ) => new(
        provider.GetRequiredService<ICardSecurityService>(),
        provider.GetRequiredService<ICvcVerifier>(),
        provider.GetRequiredService<IMerchantRepository>(),
        provider.GetRequiredService<ISavingsAccountRepository>(),
        provider.GetRequiredService<ICreditCardRepository>(),
        provider.GetRequiredService<IFinancialOperationRepository>(),
        provider.GetRequiredService<IUserRepository>(),
        provider.GetRequiredService<IUnitOfWork>(),
        provider.GetRequiredService<IBusinessClock>(),
        provider.GetRequiredService<ICurrentUserService>(),
        provider.GetRequiredService<IEmailService>(),
        provider.GetRequiredService<ILogger<ProcessHermesPayCommandHandler>>()
    );

    private static ProcessHermesPayCommand Command(decimal amount, string key, int? commerceId = 999) =>
        new(commerceId, Pan, "08", "2029", Cvc, amount, key);

    [Fact]
    public async Task ProcessPayment_Success_IncreasesDebtCreditsCommerceAndPersistsOperation() {
        AppUser commerceUser = await CreateUserAsync(CommerceUserName, nameof(Roles.Comercio));
        AppUser cardOwner = await CreateUserAsync(CardOwnerUserName, nameof(Roles.Cliente));
        Merchant merchant = await CreateMerchantAsync(commerceUser.Id);
        SavingsAccount commerceAccount = await AddPrincipalAccountAsync(commerceUser.Id, 1000m);
        int cardId = await SeedCardAsync(cardOwner.Id, limit: 10_000m);

        var currentUser = new MutableCurrentUser {
            UserId = commerceUser.Id,
            UserName = CommerceUserName,
            CommerceId = merchant.Id
        };
        await using var provider = BuildHermesProvider(currentUser);
        await using var scope = provider.CreateAsyncScope();
        var handler = CreatePaymentHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            Command(amount: 689.25m, key: "hermes-success"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Approved");

        await WithContextAsync(async context => {
            var card = await context.CreditCards.AsNoTracking()
                .SingleAsync(item => item.Id == cardId);
            card.CurrentDebt.Amount.Should().Be(689.25m);

            var account = await context.SavingsAccounts.AsNoTracking()
                .SingleAsync(item => item.Id == commerceAccount.Id);
            account.Balance.Amount.Should().Be(1689.25m);

            var operation = await context.FinancialOperations.AsNoTracking()
                .Include(item => item.AccountTransactions)
                .Include(item => item.CardConsumption)
                .SingleAsync(item => item.Id == result.Value.OperationId);
            operation.Kind.Should().Be(FinancialOperationKind.HermesPayment);
            operation.Status.Should().Be(FinancialOperationStatus.Approved);
            operation.RequestedAmount.Amount.Should().Be(689.25m);
            operation.AppliedAmount.Amount.Should().Be(689.25m);
            operation.MerchantId.Should().Be(merchant.Id);
            operation.CreditCardId.Should().Be(cardId);

            operation.AccountTransactions.Should().ContainSingle(transaction =>
                transaction.Direction == TransactionDirection.Credit
                && transaction.OriginReference == CardLastFour
                && transaction.Amount.Amount == 689.25m
            );

            Assert.NotNull(operation.CardConsumption);
            operation.CardConsumption.MerchantId.Should().Be(merchant.Id);
            operation.CardConsumption.Type.Should().Be(ConsumptionType.Purchase);
            operation.CardConsumption.Amount.Amount.Should().Be(689.25m);
        });
    }

    [Fact]
    public async Task ProcessPayment_InsufficientCredit_PersistsRejectedConsumptionWithoutStateChanges() {
        AppUser commerceUser = await CreateUserAsync(CommerceUserName, nameof(Roles.Comercio));
        AppUser cardOwner = await CreateUserAsync(CardOwnerUserName, nameof(Roles.Cliente));
        Merchant merchant = await CreateMerchantAsync(commerceUser.Id);
        SavingsAccount commerceAccount = await AddPrincipalAccountAsync(commerceUser.Id, 1000m);
        int cardId = await SeedCardAsync(cardOwner.Id, limit: 500m);

        var currentUser = new MutableCurrentUser {
            UserId = commerceUser.Id,
            UserName = CommerceUserName,
            CommerceId = merchant.Id
        };
        await using var provider = BuildHermesProvider(currentUser);
        await using var scope = provider.CreateAsyncScope();
        var handler = CreatePaymentHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            Command(amount: 689.25m, key: "hermes-insufficient"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Card.InsufficientCredit");

        await WithContextAsync(async context => {
            var card = await context.CreditCards.AsNoTracking()
                .SingleAsync(item => item.Id == cardId);
            card.CurrentDebt.Amount.Should().Be(0m);

            var account = await context.SavingsAccounts.AsNoTracking()
                .SingleAsync(item => item.Id == commerceAccount.Id);
            account.Balance.Amount.Should().Be(1000m);

            // El consumo RECHAZADO sobrevive en su propia transacción
            // confirmada (ADR-002 §2): con el UnitOfWork corregido, un
            // Result.Failure dentro del bloque atómico revertiría y el
            // historial rechazado se perdería.
            var operation = await context.FinancialOperations.AsNoTracking()
                .Include(item => item.CardConsumption)
                .SingleAsync(item => item.CreditCardId == cardId);
            operation.Kind.Should().Be(FinancialOperationKind.HermesPayment);
            operation.Status.Should().Be(FinancialOperationStatus.Rejected);
            operation.RejectionCode.Should().Be("InsufficientCredit");
            operation.AppliedAmount.Amount.Should().Be(0m);

            Assert.NotNull(operation.CardConsumption);
            operation.CardConsumption.Type.Should().Be(ConsumptionType.Purchase);
            operation.CardConsumption.MerchantId.Should().Be(merchant.Id);
        });
    }

    [Fact]
    public async Task TwoConcurrentPayments_ChargeExactlyOnce() {
        AppUser commerceUser = await CreateUserAsync(CommerceUserName, nameof(Roles.Comercio));
        AppUser cardOwner = await CreateUserAsync(CardOwnerUserName, nameof(Roles.Cliente));
        Merchant merchant = await CreateMerchantAsync(commerceUser.Id);
        SavingsAccount commerceAccount = await AddPrincipalAccountAsync(commerceUser.Id, 1000m);
        int cardId = await SeedCardAsync(cardOwner.Id, limit: 10_000m);

        var currentUser = new MutableCurrentUser {
            UserId = commerceUser.Id,
            UserName = CommerceUserName,
            CommerceId = merchant.Id
        };
        await using var providerA = BuildHermesProvider(currentUser);
        await using var providerB = BuildHermesProvider(currentUser);
        await using var scopeA = providerA.CreateAsyncScope();
        await using var scopeB = providerB.CreateAsyncScope();
        var handlerA = CreatePaymentHandler(scopeA.ServiceProvider);
        var handlerB = CreatePaymentHandler(scopeB.ServiceProvider);

        var outcomes = await Task.WhenAll(
            handlerA.Handle(Command(amount: 8_000m, key: "hermes-race-a"), CancellationToken.None).AsTask(),
            handlerB.Handle(Command(amount: 8_000m, key: "hermes-race-b"), CancellationToken.None).AsTask()
        );

        outcomes.Count(outcome => outcome.IsSuccess).Should().Be(1);
        outcomes.Count(outcome => outcome.IsFailure).Should().Be(1);

        await WithContextAsync(async context => {
            var card = await context.CreditCards.AsNoTracking()
                .SingleAsync(item => item.Id == cardId);
            card.CurrentDebt.Amount.Should().Be(8_000m);

            var account = await context.SavingsAccounts.AsNoTracking()
                .SingleAsync(item => item.Id == commerceAccount.Id);
            account.Balance.Amount.Should().Be(9_000m);

            int approvedCount = await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.HermesPayment
                && operation.Status == FinancialOperationStatus.Approved
            );
            approvedCount.Should().Be(1);
        });
    }
}
