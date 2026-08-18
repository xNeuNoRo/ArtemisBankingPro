using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class AssignSecondarySavingsAccountTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string AdminId = "secondary-account-admin";
    private int _documentCounter;

    [Fact]
    public async Task AssignSecondary_WithInitialAmount_PersistsAccountAndCredit() {
        AppUser client = await CreateClientAsync("secondaryclient1");
        await CreatePrincipalAccountAsync(client.Id);

        await using ServiceProvider provider = BuildProvider(services =>
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser())
        );
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        AssignSecondarySavingsAccountCommandHandler handler = CreateHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new AssignSecondarySavingsAccountCommand(client.Id, 1_500m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ClientId.Should().Be(client.Id);
        result.Value.Balance.Should().Be(1_500m);
        result.Value.Type.Should().Be("Secondary");

        await WithContextAsync(async context => {
            SavingsAccount account = await context.SavingsAccounts.SingleAsync(
                item => item.Number == AccountNumber.Create(result.Value.AccountNumber).Value
            );
            account.OwnerUserId.Should().Be(client.Id);
            account.Type.Should().Be(AccountType.Secondary);
            account.Status.Should().Be(AccountStatus.Active);
            account.Balance.Amount.Should().Be(1_500m);

            var operation = await context.FinancialOperations
                .Include(item => item.AccountTransactions)
                .SingleAsync(item => item.Kind == FinancialOperationKind.InitialFunding);
            operation.AppliedAmount.Amount.Should().Be(1_500m);
            operation.AccountTransactions.Should().ContainSingle();
            Assert.Equal(account.Number, operation.AccountTransactions.Single().AccountNumber);
            operation.AccountTransactions.Single().Direction.Should().Be(TransactionDirection.Credit);
        });
    }

    [Fact]
    public async Task AssignSecondary_WithoutPrincipalAccount_DoesNotPersistAnything() {
        AppUser client = await CreateClientAsync("secondaryclient2");

        await using ServiceProvider provider = BuildProvider(services =>
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser())
        );
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        var result = await CreateHandler(scope.ServiceProvider).Handle(
            new AssignSecondarySavingsAccountCommand(client.Id, 1_500m),
            CancellationToken.None
        );

        result.Error!.Code.Should().Be("Account.NoPrincipalAccount");
        await WithContextAsync(async context => {
            (await context.SavingsAccounts.AnyAsync()).Should().BeFalse();
            (await context.FinancialOperations.AnyAsync()).Should().BeFalse();
        });
    }

    [Fact]
    public async Task AssignSecondary_ToInactiveClient_DoesNotPersistSecondaryAccount() {
        AppUser client = await CreateClientAsync("secondaryclient3", active: false);
        await CreatePrincipalAccountAsync(client.Id);

        await using ServiceProvider provider = BuildProvider(services =>
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser())
        );
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        var result = await CreateHandler(scope.ServiceProvider).Handle(
            new AssignSecondarySavingsAccountCommand(client.Id, 1_500m),
            CancellationToken.None
        );

        result.Error!.Code.Should().Be("Account.CustomerNotActive");
        await WithContextAsync(async context => {
            (await context.SavingsAccounts.CountAsync()).Should().Be(1);
            (await context.SavingsAccounts.AnyAsync(
                account => account.Type == AccountType.Secondary
            )).Should().BeFalse();
            (await context.FinancialOperations.AnyAsync()).Should().BeFalse();
        });
    }

    private async Task<AppUser> CreateClientAsync(string userName, bool active = true) {
        await using AsyncServiceScope scope = Fixture.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(nameof(Roles.Cliente))) {
            (await roleManager.CreateAsync(new IdentityRole(nameof(Roles.Cliente))))
                .Succeeded.Should().BeTrue();
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Cliente",
            LastName = "Prueba",
            IdentityDocument = $"70000{(_documentCounter++):D5}",
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, nameof(Roles.Cliente))).Succeeded.Should().BeTrue();
        return user;
    }

    private async Task CreatePrincipalAccountAsync(string userId) {
        await using AsyncServiceScope scope = Fixture.Services.CreateAsyncScope();
        var generator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
        var repository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var account = SavingsAccount.OpenPrimary(
            userId,
            AccountNumber.Create(await generator.NextAccountNumberAsync()).Value,
            Money.Zero,
            AdminId,
            DateTimeOffset.UtcNow
        ).Value;

        await unitOfWork.ExecuteInTransactionAsync(async ct => {
            await repository.AddAsync(account, ct);
            return Result.Success();
        });
    }

    private static AssignSecondarySavingsAccountCommandHandler CreateHandler(
        IServiceProvider provider
    ) =>
        new(
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<IFinancialOperationRepository>(),
            provider.GetRequiredService<INumberGenerator>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IBusinessClock>(),
            provider.GetRequiredService<ICurrentUserService>()
        );

    private sealed class FixedCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => AdminId;
        public string? UserName => "secondaryadmin";
        public string? Role => nameof(Roles.Administrador);
        public int? CommerceId => null;
    }

    private async Task<string> CreateSecondaryWithBalanceAsync(string ownerUserId, decimal balance) {
        string accountNumber = "";
        await using var scope = Fixture.Services.CreateAsyncScope();
        var generator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
        var repository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string rawNumber = await generator.NextAccountNumberAsync();
        var account = SavingsAccount.OpenSecondary(
            ownerUserId,
            AccountNumber.Create(rawNumber).Value,
            Money.Zero,
            AdminId,
            DateTimeOffset.UtcNow
        ).Value;
        account.Credit(Money.Create(balance).Value).IsSuccess.Should().BeTrue();

        await unitOfWork.ExecuteInTransactionAsync(async ct => {
            await repository.AddAsync(account, ct);
            return Result.Success();
        });

        accountNumber = account.Number.Value;
        return accountNumber;
    }

    private static CancelSecondarySavingsAccountCommandHandler CreateCancelHandler(
        IServiceProvider provider
    ) =>
        new(
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<IFinancialOperationRepository>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<ICurrentUserService>(),
            provider.GetRequiredService<IBusinessClock>()
        );

    [Fact]
    public async Task DoubleCancellation_TransfersBalanceExactlyOnce() {
        AppUser client = await CreateClientAsync("cancelrace1");
        await CreatePrincipalAccountAsync(client.Id);
        string secondaryNumber = await CreateSecondaryWithBalanceAsync(client.Id, 300m);

        await using var providerA = BuildProvider(services =>
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser())
        );
        await using var providerB = BuildProvider(services =>
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser())
        );
        await using var scopeA = providerA.CreateAsyncScope();
        await using var scopeB = providerB.CreateAsyncScope();
        var handlerA = CreateCancelHandler(scopeA.ServiceProvider);
        var handlerB = CreateCancelHandler(scopeB.ServiceProvider);

        var outcomes = await Task.WhenAll(
            handlerA.Handle(
                new CancelSecondarySavingsAccountCommand(secondaryNumber),
                CancellationToken.None
            ).AsTask(),
            handlerB.Handle(
                new CancelSecondarySavingsAccountCommand(secondaryNumber),
                CancellationToken.None
            ).AsTask()
        );

        // Una sola cancelación transfiere el saldo: el perdedor obtiene
        // conflicto de concurrencia (rowversion) y el saldo nunca se
        // transfiere dos veces.
        outcomes.Count(outcome => outcome.IsSuccess).Should().Be(1);
        outcomes.Count(outcome => outcome.IsFailure).Should().Be(1);

        await WithContextAsync(async context => {
            var secondary = await context.SavingsAccounts.AsNoTracking()
                .SingleAsync(item => item.Number == AccountNumber.Create(secondaryNumber).Value);
            secondary.Status.Should().Be(AccountStatus.Cancelled);
            secondary.Balance.Amount.Should().Be(0m);

            var principal = await context.SavingsAccounts.AsNoTracking()
                .SingleAsync(item => item.OwnerUserId == client.Id && item.Type == AccountType.Primary);
            principal.Balance.Amount.Should().Be(300m);

            int transferCount = await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.SecondaryAccountClosureTransfer
                && operation.Status == FinancialOperationStatus.Approved
            );
            transferCount.Should().Be(1);
        });
    }
}
