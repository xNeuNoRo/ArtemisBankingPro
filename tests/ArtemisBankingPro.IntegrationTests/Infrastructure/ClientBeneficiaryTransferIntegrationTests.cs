using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Features.Client.Handlers;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

/// <summary>
/// Flujo de transferencia a beneficiario del cliente sobre SQL Server real:
/// la relación beneficiario→cuenta destino se resuelve contra el estado
/// vigente, una cuenta destino cancelada rechaza la transferencia sin
/// cambiar balances, y el par débito/crédito se persiste atómicamente.
/// </summary>
[Collection("SqlServer")]
public sealed class ClientBeneficiaryTransferIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string SenderUserId = "beneficiary-sender";
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.FromHours(-4));

    private int _documentCounter;

    private sealed class MutableCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId { get; set; } = SenderUserId;
        public string? UserName => "beneficiarysender";
        public string? Role => nameof(Roles.Cliente);
        public int? CommerceId => null;
    }

    private sealed class NoopEmailService : IEmailService {
        public Task SendAsync<T>(string recipient, T model, CancellationToken ct = default)
            where T : IEmailModel => Task.CompletedTask;
    }

    private async Task<AppUser> CreateClientAsync(string userName) {
        await using var scope = Fixture.Services.CreateAsyncScope();
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
            LastName = "Beneficiario",
            IdentityDocument = $"82000{(_documentCounter++):D5}",
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, nameof(Roles.Cliente))).Succeeded.Should().BeTrue();
        return user;
    }

    private async Task<SavingsAccount> AddAccountAsync(
        string ownerUserId,
        decimal balance,
        bool primary = true
    ) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var numberGenerator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
        var accountRepository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string rawNumber = await numberGenerator.NextAccountNumberAsync();
        var account = (primary
            ? SavingsAccount.OpenPrimary(
                ownerUserId,
                AccountNumber.Create(rawNumber).Value,
                Money.Create(balance).Value,
                "admin",
                IssuedAt
            )
            : SavingsAccount.OpenSecondary(
                ownerUserId,
                AccountNumber.Create(rawNumber).Value,
                Money.Create(balance).Value,
                "admin",
                IssuedAt
            )).Value;
        await unitOfWork.ExecuteInTransactionAsync(async ct => {
            await accountRepository.AddAsync(account, ct);
            return Result.Success();
        });
        return account;
    }

    private async Task<Beneficiary> CreateBeneficiaryAsync(
        string ownerUserId,
        int destinationAccountId
    ) {
        Beneficiary beneficiary = Beneficiary.Create(
            ownerUserId,
            destinationAccountId,
            IssuedAt
        ).Value;
        await WithContextAsync(async context => {
            context.Beneficiaries.Add(beneficiary);
            await context.SaveChangesAsync();
        });
        return beneficiary;
    }

    private ServiceProvider BuildClientProvider(MutableCurrentUser currentUser) =>
        BuildProvider(services => {
            services.AddScoped<ICurrentUserService>(_ => currentUser);
            services.AddScoped<IEmailService>(_ => new NoopEmailService());
        });

    private static ProcessBeneficiaryTransferCommandHandler CreateTransferHandler(
        IServiceProvider provider
    ) {
        var processor = new TransferProcessor(
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<IFinancialOperationRepository>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IBusinessClock>()
        );

        return new ProcessBeneficiaryTransferCommandHandler(
            processor,
            provider.GetRequiredService<IBeneficiaryRepository>(),
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<IBusinessClock>(),
            provider.GetRequiredService<ICurrentUserService>(),
            provider.GetRequiredService<IEmailService>(),
            provider.GetRequiredService<ILogger<ProcessBeneficiaryTransferCommandHandler>>()
        );
    }

    [Fact]
    public async Task TransferToBeneficiary_Success_DebitsSourceCreditsDestinationAtomically() {
        AppUser sender = await CreateClientAsync("bensender1");
        AppUser receiver = await CreateClientAsync("benreceiver1");
        SavingsAccount source = await AddAccountAsync(sender.Id, 1000m);
        SavingsAccount destination = await AddAccountAsync(receiver.Id, 500m);
        Beneficiary beneficiary = await CreateBeneficiaryAsync(sender.Id, destination.Id);

        await using var provider = BuildClientProvider(new MutableCurrentUser { UserId = sender.Id });
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateTransferHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new ProcessBeneficiaryTransferCommand(
                beneficiary.Id,
                source.Number.Value,
                300m,
                "ben-transfer-success"
            ),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();

        await WithContextAsync(async context => {
            var loadedSource = await context.SavingsAccounts.AsNoTracking()
                .SingleAsync(item => item.Id == source.Id);
            loadedSource.Balance.Amount.Should().Be(700m);

            var loadedDestination = await context.SavingsAccounts.AsNoTracking()
                .SingleAsync(item => item.Id == destination.Id);
            loadedDestination.Balance.Amount.Should().Be(800m);

            int debitCount = await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.BeneficiaryTransfer
                && operation.Status == FinancialOperationStatus.Approved
                && operation.AccountTransactions.Any(transaction =>
                    transaction.Direction == TransactionDirection.Debit)
            );
            int creditCount = await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.BeneficiaryTransfer
                && operation.Status == FinancialOperationStatus.Approved
                && operation.AccountTransactions.Any(transaction =>
                    transaction.Direction == TransactionDirection.Credit)
            );
            debitCount.Should().Be(1);
            creditCount.Should().Be(1);
        });
    }

    [Fact]
    public async Task TransferToBeneficiary_CancelledDestination_RejectedWithoutBalanceChanges() {
        AppUser sender = await CreateClientAsync("bensender2");
        AppUser receiver = await CreateClientAsync("benreceiver2");
        SavingsAccount source = await AddAccountAsync(sender.Id, 1000m);
        SavingsAccount destination = await AddAccountAsync(receiver.Id, 0m, primary: false);
        await WithContextAsync(async context => {
            var account = await context.SavingsAccounts.SingleAsync(item => item.Id == destination.Id);
            account.Cancel(IssuedAt.AddDays(1)).IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        });
        Beneficiary beneficiary = await CreateBeneficiaryAsync(sender.Id, destination.Id);

        await using var provider = BuildClientProvider(new MutableCurrentUser { UserId = sender.Id });
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateTransferHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new ProcessBeneficiaryTransferCommand(
                beneficiary.Id,
                source.Number.Value,
                300m,
                "ben-transfer-cancelled"
            ),
            CancellationToken.None
        );

        // Una cuenta destino cancelada nunca recibe fondos (gap de
        // beneficiarios del plan): la transferencia se rechaza y ningún
        // balance cambia.
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");

        await WithContextAsync(async context => {
            var loadedSource = await context.SavingsAccounts.AsNoTracking()
                .SingleAsync(item => item.Id == source.Id);
            loadedSource.Balance.Amount.Should().Be(1000m);

            int approvedCount = await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.BeneficiaryTransfer
                && operation.Status == FinancialOperationStatus.Approved
            );
            approvedCount.Should().Be(0);
        });
    }
}
