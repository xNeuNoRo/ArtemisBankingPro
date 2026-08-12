using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class CashierCardPaymentIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string CashierId = "cashier-card-payment";
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateOnly IssueDate = new(2026, 8, 10);

    private sealed class FixedCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => CashierId;
        public string? UserName => "cashiercardpay";
        public string? Role => "Cajero";
        public int? CommerceId => null;
    }

    private sealed class NoopEmailService : IEmailService {
        public Task SendAsync<T>(string recipient, T model, CancellationToken ct = default)
            where T : IEmailModel => Task.CompletedTask;
    }

    private int _documentCounter;
    private int _cardCounter;

    private async Task<AppUser> CreateClientWithPrincipalAccountAsync(
        string userName,
        decimal initialBalance
    ) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(nameof(Roles.Cliente))) {
            (await roleManager.CreateAsync(new IdentityRole(nameof(Roles.Cliente))))
                .Succeeded
                .Should()
                .BeTrue();
        }

        string document = $"71000{(_documentCounter++):D5}";
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Cliente",
            LastName = "Prueba",
            IdentityDocument = document,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, nameof(Roles.Cliente))).Succeeded.Should().BeTrue();

        await AddAccountAsync(user.Id, AccountType.Primary, initialBalance);

        return user;
    }

    private async Task AddAccountAsync(string ownerUserId, AccountType type, decimal initialBalance) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var numberGenerator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
        var accountRepository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string rawNumber = await numberGenerator.NextAccountNumberAsync();
        var accountNumber = AccountNumber.Create(rawNumber).Value;
        var balance = Money.Create(initialBalance).Value;
        var account = type == AccountType.Primary
            ? SavingsAccount.OpenPrimary(ownerUserId, accountNumber, balance, CashierId, DateTimeOffset.UtcNow).Value
            : SavingsAccount.OpenSecondary(ownerUserId, accountNumber, balance, CashierId, DateTimeOffset.UtcNow).Value;

        await unitOfWork.ExecuteInTransactionAsync(async ct => {
            await accountRepository.AddAsync(account, ct);
            return Result.Success();
        });
    }

    private ServiceProvider BuildProvider() =>
        Fixture.BuildProvider(configure: services => {
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser());
            services.AddScoped<IEmailService>(_ => new NoopEmailService());
        });

    private static ProcessCardPaymentCommandHandler CreatePaymentHandler(
        IServiceProvider provider
    ) =>
        new(
            provider.GetRequiredService<ICreditCardRepository>(),
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<IFinancialOperationRepository>(),
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IBusinessClock>(),
            provider.GetRequiredService<ICurrentUserService>(),
            provider.GetRequiredService<IEmailService>(),
            provider.GetRequiredService<ILogger<ProcessCardPaymentCommandHandler>>()
        );

    private async Task<int> SeedCardWithDebtAsync(string customerUserId, decimal debt) {
        int cardId = 0;
        string fingerprint = $"{_cardCounter++:x16}".PadRight(64, '0');

        await WithContextAsync(async context => {
            var card = CreditCard.Issue(
                customerUserId,
                "1234",
                fingerprint,
                CvcDigest.Create(new string('c', 64)).Value,
                Money.Create(10_000m).Value,
                "admin",
                IssuedAt,
                IssueDate
            ).Value;
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
            cardId = card.Id;
        });

        if (debt > 0m) {
            await WithContextAsync(async context => {
                var repository = new CreditCardRepository(context);
                CreditCard? card = await repository.GetByIdAsync(cardId);
                Assert.NotNull(card);
                card.AuthorizeCharge(Money.Create(debt).Value, IssueDate)
                    .IsSuccess
                    .Should()
                    .BeTrue();
                repository.Update(card);
                await context.SaveChangesAsync();
            });
        }

        return cardId;
    }

    private async Task<string> GetPrincipalNumberAsync(string ownerUserId) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var accountRepository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var principal = await accountRepository.GetPrincipalByOwnerAsync(ownerUserId);
        Assert.NotNull(principal);
        return principal.Number.Value;
    }

    [Fact]
    public async Task ProcessCardPayment_Success_DebitsAccountReducesDebtAndPersistsOperation() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("cpayclient", 200000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        int cardId = await SeedCardWithDebtAsync(client.Id, debt: 2000m);
        string accountNumber = await GetPrincipalNumberAsync(client.Id);

        var handler = CreatePaymentHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessCardPaymentCommand(cardId, accountNumber, 1000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(1000m);
        result.Value.CardLastFour.Should().Be("1234");
        result.Value.LoanNumber.Should().BeNull();
        result.Value.AccountNumber.Should().Be(accountNumber);
        result.Value.Status.Should().Be("Approved");

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var account = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(accountNumber).Value
            );
            Assert.NotNull(account);
            account.Balance.Amount.Should().Be(200000m - 1000m);

            var cardRepository = new CreditCardRepository(context);
            CreditCard? card = await cardRepository.GetByIdAsync(cardId);
            Assert.NotNull(card);
            card.Status.Should().Be(CreditCardStatus.Active);
            card.CurrentDebt.Amount.Should().Be(1000m);
            card.AvailableCredit.Amount.Should().Be(9000m);

            var operationRepository = new FinancialOperationRepository(context);
            var operation = await operationRepository.GetByIdAsync(result.Value.OperationId);
            Assert.NotNull(operation);
            operation.Kind.Should().Be(FinancialOperationKind.CreditCardPayment);
            operation.Status.Should().Be(FinancialOperationStatus.Approved);
            operation.RequestedAmount.Amount.Should().Be(1000m);
            operation.AppliedAmount.Amount.Should().Be(1000m);
            operation.InitiatedByUserId.Should().Be(CashierId);
            operation.CreditCardId.Should().Be(cardId);
            operation.AccountTransactions.Should().ContainSingle(transaction =>
                transaction.Direction == TransactionDirection.Debit
                && transaction.AccountNumber.Value == accountNumber
                && transaction.BeneficiaryReference == "1234"
                && transaction.Amount.Amount == 1000m
            );
        });
    }

    [Fact]
    public async Task ProcessCardPayment_Overpayment_DebitsOnlyRealDebt() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("cpayover", 200000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        int cardId = await SeedCardWithDebtAsync(client.Id, debt: 2000m);
        string accountNumber = await GetPrincipalNumberAsync(client.Id);

        decimal requested = 1_000_000m;
        var handler = CreatePaymentHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessCardPaymentCommand(cardId, accountNumber, requested),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(2000m);

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var account = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(accountNumber).Value
            );
            Assert.NotNull(account);
            account.Balance.Amount.Should().Be(200000m - 2000m);

            var cardRepository = new CreditCardRepository(context);
            CreditCard? card = await cardRepository.GetByIdAsync(cardId);
            Assert.NotNull(card);
            card.CurrentDebt.Should().Be(Money.Zero);
            card.AvailableCredit.Amount.Should().Be(10000m);

            var operationRepository = new FinancialOperationRepository(context);
            var operation = await operationRepository.GetByIdAsync(result.Value.OperationId);
            Assert.NotNull(operation);
            operation.RequestedAmount.Amount.Should().Be(requested);
            operation.AppliedAmount.Amount.Should().Be(2000m);
        });
    }

    [Fact]
    public async Task ProcessCardPayment_InsufficientFunds_DoesNotChangeState() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("cpaypoor", 1000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        int cardId = await SeedCardWithDebtAsync(client.Id, debt: 2000m);
        string accountNumber = await GetPrincipalNumberAsync(client.Id);

        var handler = CreatePaymentHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessCardPaymentCommand(cardId, accountNumber, 1_000_000m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InsufficientFunds");

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var account = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(accountNumber).Value
            );
            Assert.NotNull(account);
            account.Balance.Amount.Should().Be(1000m);

            var cardRepository = new CreditCardRepository(context);
            CreditCard? card = await cardRepository.GetByIdAsync(cardId);
            Assert.NotNull(card);
            card.CurrentDebt.Amount.Should().Be(2000m);

            (await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.CreditCardPayment
            )).Should().Be(0);
        });
    }

    [Fact]
    public async Task ProcessCardPayment_NoDebt_ReturnsNoDebt() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("cpaynodebt", 200000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        int cardId = await SeedCardWithDebtAsync(client.Id, debt: 0m);
        string accountNumber = await GetPrincipalNumberAsync(client.Id);

        var handler = CreatePaymentHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessCardPaymentCommand(cardId, accountNumber, 1000m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Card.NoDebt");

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var account = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(accountNumber).Value
            );
            Assert.NotNull(account);
            account.Balance.Amount.Should().Be(200000m);

            (await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.CreditCardPayment
            )).Should().Be(0);
        });
    }

    [Fact]
    public async Task ProcessCardPayment_CancelledCard_ReturnsNotActive() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("cpaycancelled", 200000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        int cardId = await SeedCardWithDebtAsync(client.Id, debt: 1000m);
        string accountNumber = await GetPrincipalNumberAsync(client.Id);

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);
            CreditCard? card = await repository.GetByIdAsync(cardId);
            Assert.NotNull(card);
            card.Cancel(IssuedAt.AddDays(1)).IsSuccess.Should().BeFalse();
            card.ApplyPayment(Money.Create(1000m).Value, IssuedAt.AddDays(1))
                .IsSuccess
                .Should()
                .BeTrue();
            card.Cancel(IssuedAt.AddDays(1)).IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        });

        var handler = CreatePaymentHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessCardPaymentCommand(cardId, accountNumber, 1000m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Card.NotActive");

        await WithContextAsync(async context => {
            (await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.CreditCardPayment
            )).Should().Be(0);
        });
    }

    [Fact]
    public async Task ProcessCardPayment_CancelledAccount_ReturnsNotActive() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("cpayacc", 200000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        int cardId = await SeedCardWithDebtAsync(client.Id, debt: 2000m);

        string cancelledNumber = await AddSecondaryAccountAsync(client.Id, 0m);
        await CancelAccountAsync(cancelledNumber);

        var handler = CreatePaymentHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessCardPaymentCommand(cardId, cancelledNumber, 1000m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");

        await WithContextAsync(async context => {
            var cardRepository = new CreditCardRepository(context);
            CreditCard? card = await cardRepository.GetByIdAsync(cardId);
            Assert.NotNull(card);
            card.CurrentDebt.Amount.Should().Be(2000m);

            (await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.CreditCardPayment
            )).Should().Be(0);
        });
    }

    private async Task<string> AddSecondaryAccountAsync(string ownerUserId, decimal initialBalance) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var numberGenerator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
        var accountRepository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string rawNumber = await numberGenerator.NextAccountNumberAsync();
        var accountNumber = AccountNumber.Create(rawNumber).Value;
        var account = SavingsAccount.OpenSecondary(
            ownerUserId,
            accountNumber,
            Money.Create(initialBalance).Value,
            CashierId,
            DateTimeOffset.UtcNow
        ).Value;

        await unitOfWork.ExecuteInTransactionAsync(async ct => {
            await accountRepository.AddAsync(account, ct);
            return Result.Success();
        });

        return accountNumber.Value;
    }

    private async Task CancelAccountAsync(string accountNumber) {
        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var account = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(accountNumber).Value
            );
            Assert.NotNull(account);
            account.Cancel(DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        });
    }
}
