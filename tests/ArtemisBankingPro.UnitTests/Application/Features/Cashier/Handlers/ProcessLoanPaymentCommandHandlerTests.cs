using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using ArtemisBankingPro.Domain.Lending.Events;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Cashier.Handlers;

public sealed class ProcessLoanPaymentCommandHandlerTests {
    private const string CashierId = "cashier-1";
    private const string ClientId = "client-1";
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly FixedToday = new(2026, 8, 7);

    private sealed class FixedCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => CashierId;
        public string? UserName => "cashier1";
        public string? Role => "Cajero";
        public int? CommerceId => null;
    }

    private static Loan SeedLoan() =>
        Loan.Issue(
            ClientId,
            LoanNumber.Create("111111111").Value,
            Money.Create(12000m).Value,
            12,
            InterestRate.Create(12m).Value,
            "admin-1",
            FixedNow,
            FixedToday
        ).Value;

    private static SavingsAccount SeedAccount(
        string ownerUserId = ClientId,
        decimal initialBalance = 50000m
    ) =>
        SavingsAccount.OpenPrimary(
            ownerUserId,
            AccountNumber.Create("100000001").Value,
            Money.Create(initialBalance).Value,
            CashierId,
            FixedNow
        ).Value;

    private static Mock<IUnitOfWork> UnitOfWork() {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result>>>(),
                It.IsAny<System.Data.IsolationLevel>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                (Func<CancellationToken, Task<Result>> operation,
                    System.Data.IsolationLevel _,
                    CancellationToken ct) => operation(ct)
            );
        return unitOfWork;
    }

    private static Mock<IBusinessClock> Clock() {
        var clock = new Mock<IBusinessClock>();
        clock.SetupGet(c => c.Now).Returns(FixedNow);
        clock.SetupGet(c => c.Today).Returns(FixedToday);
        clock.SetupGet(c => c.BusinessTimeZone).Returns(TimeZoneInfo.Utc);
        return clock;
    }

    private static Mock<IUserRepository> UserRepository() {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(r => r.GetByIdAsync(ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new UserListDto(
                    ClientId,
                    "cliente01",
                    "001",
                    "María",
                    "Gómez",
                    "maria@artemis.com",
                    "Cliente",
                    true,
                    FixedNow
                )
            );
        return repository;
    }

    private static ProcessLoanPaymentCommandHandler CreateHandler(
        out Mock<ILoanRepository> loanRepository,
        out Mock<ISavingsAccountRepository> accountRepository,
        out Mock<IFinancialOperationRepository> operationRepository,
        out Mock<IEmailService> emailService,
        Loan? loan,
        SavingsAccount? account,
        Mock<IUserRepository>? userRepository = null,
        string accountOwnerUserId = ClientId
    ) {
        loanRepository = new Mock<ILoanRepository>();
        loanRepository
            .Setup(r => r.GetWithInstallmentsByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loan);

        accountRepository = new Mock<ISavingsAccountRepository>();
        accountRepository
            .Setup(r => r.GetByNumberAsync(It.IsAny<AccountNumber>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        operationRepository = new Mock<IFinancialOperationRepository>();
        operationRepository
            .Setup(r => r.AddAsync(It.IsAny<FinancialOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinancialOperation operation, CancellationToken _) => operation);

        emailService = new Mock<IEmailService>();
        userRepository ??= UserRepository();
        if (accountOwnerUserId != ClientId) {
            userRepository
                .Setup(r => r.GetByIdAsync(accountOwnerUserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new UserListDto(
                        accountOwnerUserId,
                        "cliente02",
                        "002",
                        "Pedro",
                        "Luna",
                        "pedro@artemis.com",
                        "Cliente",
                        true,
                        FixedNow
                    )
                );
        }

        return new ProcessLoanPaymentCommandHandler(
            loanRepository.Object,
            accountRepository.Object,
            operationRepository.Object,
            userRepository.Object,
            UnitOfWork().Object,
            Clock().Object,
            new FixedCurrentUser(),
            emailService.Object,
            NullLogger<ProcessLoanPaymentCommandHandler>.Instance
        );
    }

    private static ProcessLoanPaymentCommand Command(decimal amount = 1000m) =>
        new(1, "100000001", amount);

    [Fact]
    public async Task Handle_ValidPayment_DebitsAccountAppliesToLoanAndCreatesOperation() {
        var loan = SeedLoan();
        var account = SeedAccount();
        var handler = CreateHandler(
            out var loanRepository,
            out var accountRepository,
            out var operationRepository,
            out var emailService,
            loan,
            account
        );

        var result = await handler.Handle(Command(1000m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(1000m);
        result.Value.Status.Should().Be("Approved");
        result.Value.LoanNumber.Should().Be("111111111");
        result.Value.AccountNumber.Should().Be("100000001");
        result.Value.OperationId.Should().NotBeEmpty();

        account.Balance.Amount.Should().Be(49000m);
        loan.Installments.First(item => item.Number == 1).Status.Should().Be(
            InstallmentStatus.PartiallyPaid
        );
        loan.Status.Should().Be(LoanStatus.Active);

        accountRepository.Verify(r => r.Update(account), Times.Once);
        loanRepository.Verify(r => r.Update(loan), Times.Once);
        operationRepository.Verify(
            r => r.AddAsync(It.IsAny<FinancialOperation>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
        emailService.Verify(
            s => s.SendAsync(
                "maria@artemis.com",
                It.IsAny<LoanPaymentModel>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_Overpayment_DebitsOnlyOutstandingAmount() {
        var loan = SeedLoan();
        var account = SeedAccount();
        var handler = CreateHandler(
            out _,
            out _,
            out var operationRepository,
            out _,
            loan,
            account
        );
        decimal outstanding = loan.OutstandingAmount.Amount;

        var result = await handler.Handle(Command(outstanding + 5000m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(outstanding);
        account.Balance.Amount.Should().Be(50000m - outstanding);
        loan.Status.Should().Be(LoanStatus.Completed);
        loan.CompletedAt.Should().Be(FixedNow);
        loan.DomainEvents.OfType<LoanCompletedEvent>().Should().ContainSingle();

        operationRepository.Verify(
            r => r.AddAsync(
                It.Is<FinancialOperation>(op =>
                    op.Kind == FinancialOperationKind.LoanPayment
                    && op.RequestedAmount.Amount == outstanding + 5000m
                    && op.AppliedAmount.Amount == outstanding
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_PaymentAcrossInstallments_PaysOldestFirst() {
        var loan = SeedLoan();
        var account = SeedAccount();
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            loan,
            account
        );
        Installment[] installments = loan.Installments.OrderBy(item => item.Number).ToArray();
        decimal payment = installments[0].RemainingAmount.Amount + 100m;

        var result = await handler.Handle(Command(payment), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(payment);
        installments[0].Status.Should().Be(InstallmentStatus.Paid);
        installments[1].PaidAmount.Amount.Should().Be(100m);
        installments[2].PaidAmount.Should().Be(Money.Zero);
    }

    [Fact]
    public async Task Handle_UnknownLoan_ReturnsNotFound() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            loan: null,
            account: SeedAccount()
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Loan.NotFound");
    }

    [Fact]
    public async Task Handle_CompletedLoan_ReturnsNotActive() {
        var loan = SeedLoan();
        loan.ApplyPayment(
            Money.Create(loan.OutstandingAmount.Amount).Value,
            FixedNow.AddMonths(1)
        );
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            loan,
            SeedAccount()
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Loan.NotActive");
    }

    [Fact]
    public async Task Handle_UnknownAccount_ReturnsSourceNotFound() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            SeedLoan(),
            account: null
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.SourceNotFound");
    }

    [Fact]
    public async Task Handle_CancelledAccount_ReturnsNotActive() {
        var account = SavingsAccount.OpenSecondary(
            ClientId,
            AccountNumber.Create("100000001").Value,
            Money.Zero,
            CashierId,
            FixedNow
        ).Value;
        account.Cancel(FixedNow.AddDays(1)).IsSuccess.Should().BeTrue();
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            SeedLoan(),
            account
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");
    }

    [Fact]
    public async Task Handle_InsufficientFunds_ReturnsDeclinedWithoutStateChanges() {
        var account = SeedAccount(initialBalance: 1000m);
        var loan = SeedLoan();
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            loan,
            account
        );

        var result = await handler.Handle(Command(75000m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InsufficientFunds");
        account.Balance.Amount.Should().Be(1000m);
        loan.Installments.Should().OnlyContain(item => item.PaidAmount == Money.Zero);
    }

    [Fact]
    public async Task Handle_EmailFailure_KeepsPaymentSuccessful() {
        var loan = SeedLoan();
        var account = SeedAccount();
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out var emailService,
            loan,
            account
        );
        emailService
            .Setup(s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<LoanPaymentModel>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(new EmailSendException("Pago realizado al préstamo 111111111", new IOException("smtp")));

        var result = await handler.Handle(Command(1000m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        account.Balance.Amount.Should().Be(49000m);
    }

    [Fact]
    public async Task Handle_DifferentAccountOwner_AlsoNotifiesAccountOwner() {
        var loan = SeedLoan();
        var account = SeedAccount(ownerUserId: "client-2");
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out var emailService,
            loan,
            account,
            accountOwnerUserId: "client-2"
        );

        var result = await handler.Handle(Command(1000m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        emailService.Verify(
            s => s.SendAsync(
                "maria@artemis.com",
                It.IsAny<LoanPaymentModel>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
        emailService.Verify(
            s => s.SendAsync(
                "pedro@artemis.com",
                It.IsAny<AccountDebitedForLoanPaymentModel>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public void Command_RequiresCashierOrAdministratorRoles() {
        var command = Command();

        command.RequiredRoles.Should().BeEquivalentTo("Cajero", "Administrador");
    }

    [Fact]
    public void Command_BuildsStableIdempotencyKeyWithMinuteGranularity() {
        var command = Command(1000m);

        string key = command.IdempotencyKey;
        string fingerprint = command.RequestFingerprint;

        key.Should().MatchRegex(@"^loan-payment-1-100000001-1000\.00-\d{12}$");
        fingerprint.Should().Be("1|100000001|1000.00");
    }
}
