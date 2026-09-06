using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.Handlers;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.Handlers;
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
using ArtemisBankingPro.Domain.Lending.Enums;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class CashierLoanPaymentIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string CashierId = "cashier-loan-payment";

    private sealed class FixedCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => CashierId;
        public string? UserName => "cashierloanpay";
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

    private static CreateLoanCommandHandler CreateLoanHandler(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<ILoanRepository>(),
            provider.GetRequiredService<ICreditCardRepository>(),
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<IFinancialOperationRepository>(),
            provider.GetRequiredService<INumberGenerator>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IBusinessClock>(),
            provider.GetRequiredService<ICurrentUserService>(),
            provider.GetRequiredService<IEmailService>(),
            provider.GetRequiredService<ILogger<CreateLoanCommandHandler>>()
        );

    private static ProcessLoanPaymentCommandHandler CreatePaymentHandler(IServiceProvider provider) {
        var processor = new LoanPaymentProcessor(
            provider.GetRequiredService<ILoanRepository>(),
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<IFinancialOperationRepository>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IBusinessClock>()
        );

        return new ProcessLoanPaymentCommandHandler(
            processor,
            provider.GetRequiredService<ILoanRepository>(),
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<IBusinessClock>(),
            provider.GetRequiredService<ICurrentUserService>(),
            provider.GetRequiredService<IEmailService>(),
            provider.GetRequiredService<ILogger<ProcessLoanPaymentCommandHandler>>()
        );
    }

    private async Task<(int LoanId, string LoanNumber, string AccountNumber)> SeedActiveLoanAsync(
        IServiceProvider provider,
        string clientId
    ) {
        var createLoanHandler = CreateLoanHandler(provider);
        var createResult = await createLoanHandler.Handle(
            new CreateLoanCommand(clientId, 100000m, 12, 12m, ConfirmHighRisk: true),
            CancellationToken.None
        );
        createResult.IsSuccess.Should().BeTrue();

        string accountNumber = await GetPrincipalNumberAsync(clientId);
        return (createResult.Value.LoanId, createResult.Value.LoanNumber, accountNumber);
    }

    [Fact]
    public async Task ProcessLoanPayment_Success_PaysOldestInstallmentAndPersistsOperation() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("lpayclient", 200000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        (int loanId, string loanNumber, string accountNumber) = await SeedActiveLoanAsync(
            scope.ServiceProvider,
            client.Id
        );

        var handler = CreatePaymentHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessLoanPaymentCommand(loanId, accountNumber, 8884.88m, "lp-success"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(8884.88m);
        result.Value.LoanNumber.Should().Be(loanNumber);
        result.Value.AccountNumber.Should().Be(accountNumber);
        result.Value.Status.Should().Be("Approved");

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var account = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(accountNumber).Value
            );
            Assert.NotNull(account);
            account.Balance.Amount.Should().Be(200000m + 100000m - 8884.88m);

            var loanRepository = new LoanRepository(context);
            var loan = await loanRepository.GetWithInstallmentsByIdAsync(loanId);
            Assert.NotNull(loan);
            loan.Status.Should().Be(LoanStatus.Active);
            loan.Installments.Single(item => item.Number == 1).Status.Should().Be(
                InstallmentStatus.Paid
            );
            loan.Installments.Single(item => item.Number == 2).Status.Should().Be(
                InstallmentStatus.Pending
            );

            var operationRepository = new FinancialOperationRepository(context);
            var operation = await operationRepository.GetByIdAsync(result.Value.OperationId);
            Assert.NotNull(operation);
            operation.Kind.Should().Be(FinancialOperationKind.LoanPayment);
            operation.Status.Should().Be(FinancialOperationStatus.Approved);
            operation.RequestedAmount.Amount.Should().Be(8884.88m);
            operation.AppliedAmount.Amount.Should().Be(8884.88m);
            operation.InitiatedByUserId.Should().Be(CashierId);
            Assert.NotNull(operation.LoanNumber);
            Assert.Equal(loanNumber, operation.LoanNumber.Value);
            operation.AccountTransactions.Should().ContainSingle(transaction =>
                transaction.Direction == TransactionDirection.Debit
                && transaction.AccountNumber.Value == accountNumber
                && transaction.BeneficiaryReference == loanNumber
                && transaction.Amount.Amount == 8884.88m
            );
        });
    }

    [Fact]
    public async Task ProcessLoanPayment_MultiInstallment_AllocatesToOldestThenNext() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("lpaymulti", 200000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        (int loanId, _, string accountNumber) = await SeedActiveLoanAsync(
            scope.ServiceProvider,
            client.Id
        );

        var handler = CreatePaymentHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessLoanPaymentCommand(loanId, accountNumber, 8884.88m + 100m, "lp-multi"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(8984.88m);

        await WithContextAsync(async context => {
            var loanRepository = new LoanRepository(context);
            var loan = await loanRepository.GetWithInstallmentsByIdAsync(loanId);
            Assert.NotNull(loan);
            loan.Installments.Single(item => item.Number == 1).Status.Should().Be(
                InstallmentStatus.Paid
            );
            loan.Installments.Single(item => item.Number == 2).PaidAmount.Amount.Should().Be(100m);
            loan.Installments.Single(item => item.Number == 2).Status.Should().Be(
                InstallmentStatus.PartiallyPaid
            );
        });
    }

    [Fact]
    public async Task ProcessLoanPayment_Overpayment_DebitsOnlyOutstandingAndCompletesLoan() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("lpayover", 200000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        (int loanId, string loanNumber, string accountNumber) = await SeedActiveLoanAsync(
            scope.ServiceProvider,
            client.Id
        );

        decimal requested = 1_000_000m;
        var handler = CreatePaymentHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessLoanPaymentCommand(loanId, accountNumber, requested, "lp-requested"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().BeLessThan(requested);

        await WithContextAsync(async context => {
            var accountRepository = new SavingsAccountRepository(context);
            var account = await accountRepository.GetByNumberAsync(
                AccountNumber.Create(accountNumber).Value
            );
            Assert.NotNull(account);
            account.Balance.Amount.Should().Be(200000m + 100000m - result.Value.Amount);

            var loanRepository = new LoanRepository(context);
            var loan = await loanRepository.GetWithInstallmentsByIdAsync(loanId);
            Assert.NotNull(loan);
            loan.Status.Should().Be(LoanStatus.Completed);
            loan.CompletedAt.Should().NotBeNull();
            loan.Installments.Should().OnlyContain(item => item.Status == InstallmentStatus.Paid);

            var operationRepository = new FinancialOperationRepository(context);
            var operation = await operationRepository.GetByIdAsync(result.Value.OperationId);
            Assert.NotNull(operation);
            operation.RequestedAmount.Amount.Should().Be(requested);
            operation.AppliedAmount.Amount.Should().Be(result.Value.Amount);
            operation.LoanNumber!.Value.Should().Be(loanNumber);
        });
    }

    [Fact]
    public async Task ProcessLoanPayment_InsufficientFunds_DoesNotChangeState() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("lpaypoor", 1000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        (int loanId, _, string accountNumber) = await SeedActiveLoanAsync(
            scope.ServiceProvider,
            client.Id
        );

        var handler = CreatePaymentHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessLoanPaymentCommand(loanId, accountNumber, 1_000_000m, "lp-million"),
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
            account.Balance.Amount.Should().Be(1000m + 100000m);

            var loanRepository = new LoanRepository(context);
            var loan = await loanRepository.GetWithInstallmentsByIdAsync(loanId);
            Assert.NotNull(loan);
            loan.Status.Should().Be(LoanStatus.Active);
            loan.Installments.Should().OnlyContain(item => item.PaidAmount == Money.Zero);

            (await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.LoanPayment
                && operation.Status == FinancialOperationStatus.Rejected
            )).Should().Be(1);
        });
    }

    [Fact]
    public async Task ProcessLoanPayment_CancelledAccount_ReturnsNotActive() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("lpaycancel", 200000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        (int loanId, _, string _) = await SeedActiveLoanAsync(scope.ServiceProvider, client.Id);

        string cancelledNumber = await AddSecondaryAccountAsync(client.Id, 0m);
        await CancelAccountAsync(cancelledNumber);

        var handler = CreatePaymentHandler(scope.ServiceProvider);
        var result = await handler.Handle(
            new ProcessLoanPaymentCommand(loanId, cancelledNumber, 1000m, "lp-cancelled-account"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");

        await WithContextAsync(async context => {
            var loanRepository = new LoanRepository(context);
            var loan = await loanRepository.GetWithInstallmentsByIdAsync(loanId);
            Assert.NotNull(loan);
            loan.Installments.Should().OnlyContain(item => item.PaidAmount == Money.Zero);
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

    [Fact]
    public async Task ProcessLoanPayment_CompletedLoan_ReturnsNotActive() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("lpaydone", 200000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        (int loanId, _, string accountNumber) = await SeedActiveLoanAsync(
            scope.ServiceProvider,
            client.Id
        );

        var handler = CreatePaymentHandler(scope.ServiceProvider);
        var first = await handler.Handle(
            new ProcessLoanPaymentCommand(loanId, accountNumber, 1_000_000m, "lp-completed"),
            CancellationToken.None
        );
        first.IsSuccess.Should().BeTrue();

        var second = await handler.Handle(
            new ProcessLoanPaymentCommand(loanId, accountNumber, 1000m, "lp-completed-2"),
            CancellationToken.None
        );

        second.IsFailure.Should().BeTrue();
        second.Error!.Code.Should().Be("Loan.NotActive");
    }

    private async Task<string> GetPrincipalNumberAsync(string ownerUserId) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var accountRepository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var principal = await accountRepository.GetPrincipalByOwnerAsync(ownerUserId);
        Assert.NotNull(principal);
        return principal.Number.Value;
    }
}
