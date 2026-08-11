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
public sealed class CashierThirdPartyTransferIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string CashierId = "cashier-transfer-id";

    private sealed class FixedCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => CashierId;
        public string? UserName => "cashiertransfer";
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

        string document = $"70000{(_documentCounter++):D5}";
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

    private async Task<string> AddAccountAsync(
        string ownerUserId,
        AccountType type,
        decimal initialBalance
    ) {
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

        return accountNumber.Value;
    }

    private ServiceProvider BuildProvider() =>
        Fixture.BuildProvider(configure: services => {
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser());
            services.AddScoped<IEmailService>(_ => new NoopEmailService());
        });

    private static ProcessThirdPartyTransferCommandHandler CreateHandler(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<IFinancialOperationRepository>(),
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IBusinessClock>(),
            provider.GetRequiredService<ICurrentUserService>(),
            provider.GetRequiredService<IEmailService>(),
            provider.GetRequiredService<ILogger<ProcessThirdPartyTransferCommandHandler>>()
        );

    [Fact]
    public async Task ProcessThirdPartyTransfer_Success_UpdatesBalancesAndPersistsPairedHistory() {
        AppUser sourceClient = await CreateClientWithPrincipalAccountAsync("tptsource", 1000m);
        AppUser destinationClient = await CreateClientWithPrincipalAccountAsync("tptdest", 500m);

        string sourceNumber = await GetPrincipalNumberAsync(sourceClient.Id);
        string destinationNumber = await GetPrincipalNumberAsync(destinationClient.Id);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new ProcessThirdPartyTransferCommand(sourceNumber, destinationNumber, 200m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.OperationId.Should().NotBeEmpty();
        result.Value.Amount.Should().Be(200m);
        result.Value.Status.Should().Be("Approved");

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var source = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(sourceNumber).Value
            );
            Assert.NotNull(source);
            source.Balance.Amount.Should().Be(800m);

            var destination = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(destinationNumber).Value
            );
            Assert.NotNull(destination);
            destination.Balance.Amount.Should().Be(700m);

            var operationRepository = new FinancialOperationRepository(context);
            var operation = await operationRepository.GetByIdAsync(result.Value.OperationId);
            Assert.NotNull(operation);
            operation.Kind.Should().Be(FinancialOperationKind.CashierTransfer);
            operation.Status.Should().Be(FinancialOperationStatus.Approved);
            operation.RequestedAmount.Amount.Should().Be(200m);
            operation.AppliedAmount.Amount.Should().Be(200m);
            operation.InitiatedByUserId.Should().Be(CashierId);

            operation.AccountTransactions.Should().HaveCount(2);
            operation.AccountTransactions.Should().Contain(transaction =>
                transaction.Direction == TransactionDirection.Debit
                && transaction.AccountNumber.Value == sourceNumber
                && transaction.Amount.Amount == 200m
            );
            operation.AccountTransactions.Should().Contain(transaction =>
                transaction.Direction == TransactionDirection.Credit
                && transaction.AccountNumber.Value == destinationNumber
                && transaction.Amount.Amount == 200m
            );
        });
    }

    [Fact]
    public async Task ProcessThirdPartyTransfer_InsufficientFunds_DoesNotChangeState() {
        AppUser sourceClient = await CreateClientWithPrincipalAccountAsync("tptpoor", 50m);
        AppUser destinationClient = await CreateClientWithPrincipalAccountAsync("tptrich", 500m);

        string sourceNumber = await GetPrincipalNumberAsync(sourceClient.Id);
        string destinationNumber = await GetPrincipalNumberAsync(destinationClient.Id);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new ProcessThirdPartyTransferCommand(sourceNumber, destinationNumber, 200m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InsufficientFunds");

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var source = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(sourceNumber).Value
            );
            Assert.NotNull(source);
            source.Balance.Amount.Should().Be(50m);

            var destination = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(destinationNumber).Value
            );
            Assert.NotNull(destination);
            destination.Balance.Amount.Should().Be(500m);

            (await context.FinancialOperations.CountAsync()).Should().Be(0);
        });
    }

    [Fact]
    public async Task ProcessThirdPartyTransfer_DestinationOwnedBySameClient_ReturnsFailure() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("tptsame", 1000m);
        string secondaryNumber = await AddAccountAsync(client.Id, AccountType.Secondary, 300m);
        string principalNumber = await GetPrincipalNumberAsync(client.Id);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new ProcessThirdPartyTransferCommand(principalNumber, secondaryNumber, 200m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Operation.DestinationMustBeThirdParty");

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var principal = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(principalNumber).Value
            );
            Assert.NotNull(principal);
            principal.Balance.Amount.Should().Be(1000m);

            var secondary = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(secondaryNumber).Value
            );
            Assert.NotNull(secondary);
            secondary.Balance.Amount.Should().Be(300m);

            (await context.FinancialOperations.CountAsync()).Should().Be(0);
        });
    }

    [Fact]
    public async Task ProcessThirdPartyTransfer_CancelledDestination_ReturnsFailure() {
        AppUser sourceClient = await CreateClientWithPrincipalAccountAsync("tptsrc2", 1000m);
        AppUser destinationClient = await CreateClientWithPrincipalAccountAsync("tptdst2", 300m);
        string secondaryNumber = await AddAccountAsync(
            destinationClient.Id,
            AccountType.Secondary,
            0m
        );

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var secondary = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(secondaryNumber).Value
            );
            Assert.NotNull(secondary);
            secondary.Cancel(DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        });

        string sourceNumber = await GetPrincipalNumberAsync(sourceClient.Id);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new ProcessThirdPartyTransferCommand(sourceNumber, secondaryNumber, 200m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var source = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(sourceNumber).Value
            );
            Assert.NotNull(source);
            source.Balance.Amount.Should().Be(1000m);

            (await context.FinancialOperations.CountAsync()).Should().Be(0);
        });
    }

    private async Task<string> GetPrincipalNumberAsync(string ownerUserId) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var accountRepository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var principal = await accountRepository.GetPrincipalByOwnerAsync(ownerUserId);
        Assert.NotNull(principal);
        return principal.Number.Value;
    }
}
