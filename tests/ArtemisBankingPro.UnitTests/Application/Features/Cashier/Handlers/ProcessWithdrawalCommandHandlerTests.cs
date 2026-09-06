using ArtemisBankingPro.Application.Common;
using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.Handlers;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Domain.Operations.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Cashier.Handlers;

public sealed class ProcessWithdrawalCommandHandlerTests {
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

    private static ProcessWithdrawalCommandHandler CreateHandler(
        out Mock<ISavingsAccountRepository> accountRepository,
        out Mock<IFinancialOperationRepository> operationRepository,
        out Mock<IEmailService> emailService,
        out Func<FinancialOperation?> addedOperation,
        SavingsAccount? account,
        Mock<IUserRepository>? userRepository = null
    ) {
        accountRepository = new Mock<ISavingsAccountRepository>();
        accountRepository
            .Setup(r => r.GetByNumberAsync(It.IsAny<AccountNumber>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        operationRepository = new Mock<IFinancialOperationRepository>();
        FinancialOperation? added = null;
        operationRepository
            .Setup(r => r.AddAsync(It.IsAny<FinancialOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (FinancialOperation operation, CancellationToken _) => {
                    added = operation;
                    return operation;
                }
            );
        addedOperation = () => added;

        emailService = new Mock<IEmailService>();
        userRepository ??= UserRepository();

        var processor = new WithdrawalProcessor(
            accountRepository.Object,
            operationRepository.Object,
            UnitOfWork().Object,
            Clock().Object
        );

        return new ProcessWithdrawalCommandHandler(
            processor,
            accountRepository.Object,
            userRepository.Object,
            Clock().Object,
            new FixedCurrentUser(),
            emailService.Object,
            NullLogger<ProcessWithdrawalCommandHandler>.Instance
        );
    }

    private static ProcessWithdrawalCommand Command(decimal amount = 1000m) =>
        new("100000001", amount, "test-key");

    [Fact]
    public async Task Handle_ValidWithdrawal_DebitsAccountCreatesOperationAndSendsEmail() {
        var account = SeedAccount();
        var handler = CreateHandler(
            out var accountRepository,
            out var operationRepository,
            out var emailService,
            out var addedOperation,
            account
        );

        var result = await handler.Handle(Command(1000m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(1000m);
        result.Value.Status.Should().Be("Approved");
        result.Value.AccountNumber.Should().Be("100000001");
        result.Value.LoanNumber.Should().BeNull();
        result.Value.CardLastFour.Should().BeNull();
        result.Value.OperationId.Should().NotBeEmpty();

        account.Balance.Amount.Should().Be(49000m);

        accountRepository.Verify(r => r.Update(account), Times.Once);
        operationRepository.Verify(
            r => r.AddAsync(It.IsAny<FinancialOperation>(), It.IsAny<CancellationToken>()),
            Times.Once
        );

        FinancialOperation operation = addedOperation()!;
        operation.Kind.Should().Be(FinancialOperationKind.Withdrawal);
        operation.Status.Should().Be(FinancialOperationStatus.Approved);
        operation.RequestedAmount.Amount.Should().Be(1000m);
        operation.AppliedAmount.Amount.Should().Be(1000m);
        operation.InitiatedByUserId.Should().Be(CashierId);
        operation.AccountTransactions.Should().ContainSingle(transaction =>
            transaction.Direction == TransactionDirection.Debit
            && transaction.AccountNumber.Value == "100000001"
            && transaction.OriginReference == "100000001"
            && transaction.BeneficiaryReference == "RETIRO"
            && transaction.Amount.Amount == 1000m
        );

        WithdrawalProcessedEvent? domainEvent = Assert.IsType<WithdrawalProcessedEvent>(
            operation.DomainEvents.OfType<WithdrawalProcessedEvent>().Single()
        );
        domainEvent.AccountNumber.Should().Be("100000001");
        domainEvent.OwnerUserId.Should().Be(ClientId);
        domainEvent.CashierUserId.Should().Be(CashierId);
        domainEvent.Amount.Amount.Should().Be(1000m);
        domainEvent.OccurredAt.Should().Be(FixedNow);

        emailService.Verify(
            s => s.SendAsync(
                "maria@artemis.com",
                It.IsAny<WithdrawalModel>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_UnknownAccount_ReturnsSourceNotFound() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
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
            account
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");
    }

    [Fact]
    public async Task Handle_InsufficientFunds_ReturnsDeclinedAndPersistsRejection() {
        var account = SeedAccount(initialBalance: 1000m);
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out var addedOperation,
            account
        );

        var result = await handler.Handle(Command(5000m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InsufficientFunds");
        account.Balance.Amount.Should().Be(1000m);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Account.InsufficientFunds");
        operation.AccountTransactions.Should().ContainSingle(transaction =>
            transaction.Direction == TransactionDirection.Debit
            && transaction.OriginReference == "100000001"
            && transaction.BeneficiaryReference == "RETIRO"
            && transaction.Amount.Amount == 5000m
        );
    }

    [Fact]
    public async Task Handle_EmailFailure_KeepsWithdrawalSuccessful() {
        var account = SeedAccount();
        var handler = CreateHandler(
            out _,
            out _,
            out var emailService,
            out _,
            account
        );
        emailService
            .Setup(s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<WithdrawalModel>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(new EmailSendException("Retiro realizado desde su cuenta 0001", new IOException("smtp")));

        var result = await handler.Handle(Command(1000m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NotificationWarning.Should().Be(NotificationMessages.EmailFailed);
        account.Balance.Amount.Should().Be(49000m);
    }

    [Fact]
    public void Command_RequiresCashierRole() {
        var command = Command();

        command.RequiredRoles.Should().Equal("Cajero");
    }

    [Fact]
    public void Command_CarriesCallerSuppliedIdempotencyKeyAndStableFingerprint() {
        var command = Command(1000m);

        command.IdempotencyKey.Should().Be("test-key");
        command.RequestFingerprint.Should().Be("100000001|1000.00");
    }
}
