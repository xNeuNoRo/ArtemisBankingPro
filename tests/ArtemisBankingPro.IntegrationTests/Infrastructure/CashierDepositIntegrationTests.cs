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
public sealed class CashierDepositIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string CashierId = "cashier-deposit";

    private sealed class FixedCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => CashierId;
        public string? UserName => "cashierdeposit";
        public string? Role => "Cajero";
        public int? CommerceId => null;
    }

    private sealed class NoopEmailService : IEmailService {
        public Task SendAsync<T>(string recipient, T model, CancellationToken ct = default)
            where T : IEmailModel => Task.CompletedTask;
    }

    private int _documentCounter;

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

        string document = $"73000{(_documentCounter++):D5}";
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

    private static ProcessDepositCommandHandler CreateDepositHandler(
        IServiceProvider provider
    ) =>
        new(
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<IFinancialOperationRepository>(),
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IBusinessClock>(),
            provider.GetRequiredService<ICurrentUserService>(),
            provider.GetRequiredService<IEmailService>(),
            provider.GetRequiredService<ILogger<ProcessDepositCommandHandler>>()
        );

    private async Task<string> GetPrincipalNumberAsync(string ownerUserId) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var accountRepository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var principal = await accountRepository.GetPrincipalByOwnerAsync(ownerUserId);
        Assert.NotNull(principal);
        return principal.Number.Value;
    }

    [Fact]
    public async Task ProcessDeposit_Success_CreditsAccountAndPersistsOperation() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("depclient", 10000m);
        string accountNumber = await GetPrincipalNumberAsync(client.Id);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateDepositHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessDepositCommand(accountNumber, 5000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(5000m);
        result.Value.AccountNumber.Should().Be(accountNumber);
        result.Value.LoanNumber.Should().BeNull();
        result.Value.CardLastFour.Should().BeNull();
        result.Value.Status.Should().Be("Approved");

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var account = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(accountNumber).Value
            );
            Assert.NotNull(account);
            account.Balance.Amount.Should().Be(10000m + 5000m);

            var operationRepository = new FinancialOperationRepository(context);
            var operation = await operationRepository.GetByIdAsync(result.Value.OperationId);
            Assert.NotNull(operation);
            operation.Kind.Should().Be(FinancialOperationKind.Deposit);
            operation.Status.Should().Be(FinancialOperationStatus.Approved);
            operation.RequestedAmount.Amount.Should().Be(5000m);
            operation.AppliedAmount.Amount.Should().Be(5000m);
            operation.InitiatedByUserId.Should().Be(CashierId);
            operation.AccountTransactions.Should().ContainSingle(transaction =>
                transaction.Direction == TransactionDirection.Credit
                && transaction.AccountNumber.Value == accountNumber
                && transaction.OriginReference == "DEPÓSITO"
                && transaction.BeneficiaryReference == accountNumber
                && transaction.Amount.Amount == 5000m
            );
        });
    }

    [Fact]
    public async Task ProcessDeposit_NonPositiveAmount_DoesNotChangeState() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("depzero", 10000m);
        string accountNumber = await GetPrincipalNumberAsync(client.Id);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateDepositHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessDepositCommand(accountNumber, 0m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.AmountMustBePositive");

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var account = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(accountNumber).Value
            );
            Assert.NotNull(account);
            account.Balance.Amount.Should().Be(10000m);

            (await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.Deposit
            )).Should().Be(0);
        });
    }

    [Fact]
    public async Task ProcessDeposit_CancelledAccount_ReturnsNotActive() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("depcancelled", 200000m);
        string accountNumber = await AddSecondaryAccountAsync(client.Id, 0m);
        await CancelAccountAsync(accountNumber);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateDepositHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessDepositCommand(accountNumber, 1000m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");

        await WithContextAsync(async context => {
            (await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.Deposit
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
