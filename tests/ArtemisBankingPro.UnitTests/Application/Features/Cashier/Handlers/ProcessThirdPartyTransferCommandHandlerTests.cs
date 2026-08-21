using ArtemisBankingPro.Application.Common;
using System.Data;
using ArtemisBankingPro.Application.Common.Interfaces;
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
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Domain.Operations.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Cashier.Handlers;

public sealed class ProcessThirdPartyTransferCommandHandlerTests {
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 11, 9, 0, 0, TimeSpan.FromHours(-4));

    private const string CashierId = "cashier-1";
    private const string SourceNumberValue = "100000001";
    private const string DestinationNumberValue = "100000002";

    private static UserListDto SourceOwner() =>
        new(
            "client-source",
            "clientesource",
            "001",
            "María",
            "Gómez",
            "maria@artemis.com",
            "Cliente",
            true,
            FixedNow
        );

    private static UserListDto DestinationOwner() =>
        new(
            "client-dest",
            "clientedest",
            "002",
            "Juan",
            "Pérez",
            "juan@artemis.com",
            "Cliente",
            true,
            FixedNow
        );

    private static void SetId(SavingsAccount account, int id) =>
        typeof(SavingsAccount)
            .GetProperty(nameof(SavingsAccount.Id))!
            .GetSetMethod(true)!
            .Invoke(account, [id]);

    private static SavingsAccount Account(
        string number,
        string ownerUserId,
        decimal balance,
        AccountStatus status = AccountStatus.Active,
        int id = 1
    ) {
        SavingsAccount account =
            status == AccountStatus.Cancelled
                ? OpenCancelledAccount(number, ownerUserId)
                : SavingsAccount
                    .OpenPrimary(
                        ownerUserId,
                        AccountNumber.Create(number).Value,
                        Money.Create(balance).Value,
                        CashierId,
                        FixedNow
                    )
                    .Value;
        SetId(account, id);
        return account;
    }

    private static SavingsAccount OpenCancelledAccount(string number, string ownerUserId) {
        var account = SavingsAccount
            .OpenSecondary(
                ownerUserId,
                AccountNumber.Create(number).Value,
                Money.Zero,
                CashierId,
                FixedNow
            )
            .Value;
        account.Cancel(FixedNow).IsSuccess.Should().BeTrue();
        return account;
    }

    private static Mock<ISavingsAccountRepository> AccountRepository(
        SavingsAccount? source,
        SavingsAccount? destination
    ) {
        var repository = new Mock<ISavingsAccountRepository>();
        repository
            .Setup(r => r.GetByNumberAsync(
                It.Is<AccountNumber>(number => number.Value == SourceNumberValue),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(source);
        repository
            .Setup(r => r.GetByNumberAsync(
                It.Is<AccountNumber>(number => number.Value == DestinationNumberValue),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(destination);
        return repository;
    }

    private static Mock<IFinancialOperationRepository> OperationRepository(
        out Func<FinancialOperation?> addedOperation
    ) {
        FinancialOperation? added = null;
        var repository = new Mock<IFinancialOperationRepository>();
        repository
            .Setup(r => r.AddAsync(It.IsAny<FinancialOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (FinancialOperation operation, CancellationToken _) => {
                    added = operation;
                    return operation;
                }
            );
        addedOperation = () => added;
        return repository;
    }

    private static Mock<IUserRepository> UserRepository(
        UserListDto? sourceOwner = null,
        UserListDto? destinationOwner = null
    ) {
        var repository = new Mock<IUserRepository>();
        IReadOnlyList<UserListDto> owners =
        [
            sourceOwner ?? SourceOwner(),
            destinationOwner ?? DestinationOwner(),
        ];
        repository
            .Setup(r => r.GetByIdsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(owners);
        return repository;
    }

    private static Mock<IUnitOfWork> UnitOfWork() {
        var uow = new Mock<IUnitOfWork>();
        uow
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result>>>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                (Func<CancellationToken, Task<Result>> operation,
                    IsolationLevel _,
                    CancellationToken ct) => operation(ct)
            );
        return uow;
    }

    private static Mock<IBusinessClock> Clock() {
        var clock = new Mock<IBusinessClock>();
        clock.SetupGet(c => c.Now).Returns(FixedNow);
        clock.SetupGet(c => c.BusinessTimeZone).Returns(TimeZoneInfo.Utc);
        return clock;
    }

    private static Mock<ICurrentUserService> CurrentUser() {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.UserId).Returns(CashierId);
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        return user;
    }

    private static ProcessThirdPartyTransferCommandHandler CreateHandler(
        out Func<FinancialOperation?> addedOperation,
        Mock<ISavingsAccountRepository>? accountRepository = null,
        Mock<IUserRepository>? userRepository = null,
        Mock<IEmailService>? emailService = null
    ) {
        var operationRepository = OperationRepository(out addedOperation);
        var unitOfWork = UnitOfWork();
        var clock = Clock();
        var accountRepositoryMock = accountRepository ?? AccountRepository(
            Account(SourceNumberValue, "client-source", 1_000m, id: 1),
            Account(DestinationNumberValue, "client-dest", 500m, id: 2)
        );
        var processor = new TransferProcessor(
            accountRepositoryMock.Object,
            operationRepository.Object,
            unitOfWork.Object,
            clock.Object
        );

        return new ProcessThirdPartyTransferCommandHandler(
            processor,
            accountRepositoryMock.Object,
            (userRepository ?? UserRepository()).Object,
            clock.Object,
            CurrentUser().Object,
            (emailService ?? new Mock<IEmailService>()).Object,
            NullLogger<ProcessThirdPartyTransferCommandHandler>.Instance
        );
    }

    private static ProcessThirdPartyTransferCommand Command(decimal amount = 200m) =>
        new(SourceNumberValue, DestinationNumberValue, amount, "test-key");

    [Fact]
    public async Task Handle_ValidTransfer_DebitsSourceCreditsDestinationAtomically() {
        SavingsAccount source = Account(SourceNumberValue, "client-source", 1_000m, id: 1);
        SavingsAccount destination = Account(DestinationNumberValue, "client-dest", 500m, id: 2);
        var accountRepository = AccountRepository(source, destination);
        var handler = CreateHandler(
            out Func<FinancialOperation?> addedOperation,
            accountRepository
        );

        var result = await handler.Handle(Command(200m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.OperationId.Should().NotBeEmpty();
        result.Value.SourceAccountNumber.Should().Be(SourceNumberValue);
        result.Value.DestinationAccountNumber.Should().Be(DestinationNumberValue);
        result.Value.Amount.Should().Be(200m);
        result.Value.OccurredAt.Should().Be(FixedNow);
        result.Value.Status.Should().Be("Approved");

        source.Balance.Amount.Should().Be(800m);
        destination.Balance.Amount.Should().Be(700m);

        var operation = addedOperation();
        Assert.NotNull(operation);
        operation.Id.Should().Be(result.Value.OperationId);
        operation.Kind.Should().Be(FinancialOperationKind.CashierTransfer);
        operation.Status.Should().Be(FinancialOperationStatus.Approved);
        operation.RequestedAmount.Should().Be(Money.Create(200m).Value);
        operation.AppliedAmount.Should().Be(Money.Create(200m).Value);
        operation.InitiatedByUserId.Should().Be(CashierId);
        operation.AccountTransactions.Should().HaveCount(2);

        // Débito y crédito pareados con el mismo correlation ID.
        var debit = operation.AccountTransactions.Single(transaction =>
            transaction.AccountNumber.Value == SourceNumberValue
        );
        var credit = operation.AccountTransactions.Single(transaction =>
            transaction.AccountNumber.Value == DestinationNumberValue
        );
        debit.Direction.Should().Be(TransactionDirection.Debit);
        credit.Direction.Should().Be(TransactionDirection.Credit);
        debit.Amount.Should().Be(Money.Create(200m).Value);
        credit.Amount.Should().Be(Money.Create(200m).Value);
        debit.FinancialOperationId.Should().Be(operation.Id);
        credit.FinancialOperationId.Should().Be(operation.Id);
    }

    [Fact]
    public async Task Handle_ValidTransfer_UpdatesRowsInStableIdOrder() {
        SavingsAccount source = Account(SourceNumberValue, "client-source", 1_000m, id: 2);
        SavingsAccount destination = Account(DestinationNumberValue, "client-dest", 500m, id: 1);
        var accountRepository = AccountRepository(source, destination);
        var handler = CreateHandler(out _, accountRepository);

        // El destino (Id 1) debe actualizarse antes que el origen (Id 2).
        var sequence = new MockSequence();
        accountRepository.InSequence(sequence).Setup(r => r.Update(destination));
        accountRepository.InSequence(sequence).Setup(r => r.Update(source));

        var result = await handler.Handle(Command(200m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        accountRepository.Verify(r => r.Update(source), Times.Once);
        accountRepository.Verify(r => r.Update(destination), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidTransfer_RaisesProcessedEvent() {
        var handler = CreateHandler(out Func<FinancialOperation?> addedOperation);

        var result = await handler.Handle(Command(200m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var operation = addedOperation();
        var processedEvent = operation!
            .DomainEvents
            .OfType<ThirdPartyTransferProcessedEvent>()
            .Single();
        processedEvent.OperationId.Should().Be(operation.Id);
        processedEvent.SourceAccountNumber.Should().Be(SourceNumberValue);
        processedEvent.DestinationAccountNumber.Should().Be(DestinationNumberValue);
        processedEvent.Amount.Should().Be(Money.Create(200m).Value);
        processedEvent.SourceOwnerUserId.Should().Be("client-source");
        processedEvent.DestinationOwnerUserId.Should().Be("client-dest");
        processedEvent.CashierUserId.Should().Be(CashierId);
    }

    [Fact]
    public async Task Handle_ValidTransfer_SendsEmailsToBothOwnersAfterCommit() {
        var emailService = new Mock<IEmailService>();
        var handler = CreateHandler(out _, emailService: emailService);

        var result = await handler.Handle(Command(200m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        emailService.Verify(
            service => service.SendAsync(
                "maria@artemis.com",
                It.Is<ThirdPartyTransferSenderModel>(model =>
                    model.CustomerName == "María Gómez"
                    && model.Amount.Amount == 200m
                    && model.SourceLastFour == "0001"
                    && model.DestinationLastFour == "0002"
                    && model.OccurredAt == FixedNow
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
        emailService.Verify(
            service => service.SendAsync(
                "juan@artemis.com",
                It.Is<ThirdPartyTransferReceiverModel>(model =>
                    model.CustomerName == "Juan Pérez"
                    && model.Amount.Amount == 200m
                    && model.SourceLastFour == "0001"
                    && model.DestinationLastFour == "0002"
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_EmailFailure_KeepsTransferSuccessful() {
        var emailService = new Mock<IEmailService>();
        emailService
            .Setup(service => service.SendAsync(
                It.IsAny<string>(),
                It.IsAny<ThirdPartyTransferSenderModel>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(
                new EmailSendException(
                    "Transacción realizada",
                    new InvalidOperationException("Fallo simulado del proveedor.")
                )
            );
        emailService
            .Setup(service => service.SendAsync(
                It.IsAny<string>(),
                It.IsAny<ThirdPartyTransferReceiverModel>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(
                new EmailSendException(
                    "Transacción enviada",
                    new InvalidOperationException("Fallo simulado del proveedor.")
                )
            );
        var handler = CreateHandler(
            out Func<FinancialOperation?> addedOperation,
            emailService: emailService
        );

        var result = await handler.Handle(Command(200m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NotificationWarning.Should().Be(NotificationMessages.EmailFailed);
        Assert.NotNull(addedOperation());
    }

    [Fact]
    public async Task Handle_SameAccount_ReturnsValidationWithoutPersistence() {
        var handler = CreateHandler(out Func<FinancialOperation?> addedOperation);

        var result = await handler.Handle(
            new ProcessThirdPartyTransferCommand(SourceNumberValue, SourceNumberValue, 200m, "test-key"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Operation.SameAccount");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task Handle_SourceNotFound_ReturnsNotFoundWithoutPersistence() {
        var accountRepository = AccountRepository(null, null);
        var handler = CreateHandler(
            out Func<FinancialOperation?> addedOperation,
            accountRepository
        );

        var result = await handler.Handle(Command(200m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.SourceNotFound");
        result.Error.Category.Should().Be(ErrorCategory.NotFound);
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task Handle_DestinationNotFound_ReturnsNotFoundWithoutPersistence() {
        var accountRepository = AccountRepository(
            Account(SourceNumberValue, "client-source", 1_000m, id: 1),
            null
        );
        var handler = CreateHandler(
            out Func<FinancialOperation?> addedOperation,
            accountRepository
        );

        var result = await handler.Handle(Command(200m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.DestinationNotFound");
        result.Error.Category.Should().Be(ErrorCategory.NotFound);
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task Handle_InactiveSource_RecordsRejectedOperation() {
        var accountRepository = AccountRepository(
            Account(SourceNumberValue, "client-source", 1_000m, id: 1, status: AccountStatus.Cancelled),
            Account(DestinationNumberValue, "client-dest", 500m, id: 2)
        );
        var handler = CreateHandler(
            out Func<FinancialOperation?> addedOperation,
            accountRepository
        );

        var result = await handler.Handle(Command(200m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
        FinancialOperation operation = addedOperation()!;
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Account.NotActive");
    }

    [Fact]
    public async Task Handle_InactiveDestination_RecordsRejectedOperation() {
        var accountRepository = AccountRepository(
            Account(SourceNumberValue, "client-source", 1_000m, id: 1),
            Account(DestinationNumberValue, "client-dest", 500m, id: 2, status: AccountStatus.Cancelled)
        );
        var handler = CreateHandler(
            out Func<FinancialOperation?> addedOperation,
            accountRepository
        );

        var result = await handler.Handle(Command(200m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
        FinancialOperation operation = addedOperation()!;
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Account.NotActive");
    }

    [Fact]
    public async Task Handle_SameOwner_ReturnsDestinationMustBeThirdPartyWithoutPersistence() {
        var accountRepository = AccountRepository(
            Account(SourceNumberValue, "client-source", 1_000m, id: 1),
            Account(DestinationNumberValue, "client-source", 500m, id: 2)
        );
        var handler = CreateHandler(
            out Func<FinancialOperation?> addedOperation,
            accountRepository
        );

        var result = await handler.Handle(Command(200m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Operation.DestinationMustBeThirdParty");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task Handle_InsufficientFunds_ReturnsDeclinedAndPersistsRejection() {
        var accountRepository = AccountRepository(
            Account(SourceNumberValue, "client-source", 50m, id: 1),
            Account(DestinationNumberValue, "client-dest", 500m, id: 2)
        );
        var handler = CreateHandler(
            out Func<FinancialOperation?> addedOperation,
            accountRepository
        );

        var result = await handler.Handle(Command(200m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InsufficientFunds");
        result.Error.Category.Should().Be(ErrorCategory.Declined);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.Kind.Should().Be(FinancialOperationKind.CashierTransfer);
        operation.RejectionCode.Should().Be("Account.InsufficientFunds");
        operation.AccountTransactions.Should().ContainSingle(t =>
            t.Direction == TransactionDirection.Debit && t.Amount.Amount == 200m
        );
    }

    [Fact]
    public void Command_CarriesCallerSuppliedIdempotencyKeyAndStableFingerprint() {
        var command = new ProcessThirdPartyTransferCommand(
            SourceNumberValue,
            DestinationNumberValue,
            200m,
            "test-key"
        );

        command.IdempotencyKey.Should().Be("test-key");
        command.RequestFingerprint.Should().Be("100000001|100000002|200.00");
    }

    [Fact]
    public void Command_RequiresCajeroRole() {
        var command = Command();

        command.RequiredRoles.Should().Equal("Cajero");
        (command is IAuthorize).Should().BeTrue();
        (command is IIdempotentCommand).Should().BeTrue();
    }
}
