using System.Data;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Domain.Operations.Events;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.FinancialProcessors;

public sealed class TransferProcessorTests {
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 11, 9, 0, 0, TimeSpan.FromHours(-4));

    private const string CashierId = "cashier-1";

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
        return clock;
    }

    private static TransferProcessor CreateProcessor(
        Mock<IFinancialOperationRepository>? operationRepository = null,
        Mock<ISavingsAccountRepository>? accountRepository = null
    ) {
        var operationRepositoryMock = operationRepository ?? OperationRepository(out _);
        var accountRepositoryMock = accountRepository ?? new Mock<ISavingsAccountRepository>();
        var unitOfWork = UnitOfWork();
        var clock = Clock();
        return new TransferProcessor(
            accountRepositoryMock.Object,
            operationRepositoryMock.Object,
            unitOfWork.Object,
            clock.Object
        );
    }

    private static Money MoneyOf(decimal amount) => Money.Create(amount).Value;

    [Fact]
    public async Task TransferAsync_Success_DebitsSourceCreditsDestinationAndCreatesPairedOperation() {
        SavingsAccount source = Account("100000001", "client-source", 1_000m, id: 1);
        SavingsAccount destination = Account("100000002", "client-dest", 500m, id: 2);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        TransferProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.TransferAsync(
            source,
            destination,
            MoneyOf(200m),
            TransferFlow.OwnAccounts,
            CashierId
        );

        result.IsSuccess.Should().BeTrue();
        source.Balance.Amount.Should().Be(800m);
        destination.Balance.Amount.Should().Be(700m);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.Status.Should().Be(FinancialOperationStatus.Approved);
        operation.Kind.Should().Be(FinancialOperationKind.OwnAccountTransfer);
        operation.RequestedAmount.Amount.Should().Be(200m);
        operation.AppliedAmount.Amount.Should().Be(200m);
        operation.InterestAmount.Amount.Should().Be(0m);
        operation.AccountTransactions.Should().HaveCount(2);
        operation.AccountTransactions.Should().ContainSingle(t =>
            t.Direction == TransactionDirection.Debit && t.Amount.Amount == 200m
        );
        operation.AccountTransactions.Should().ContainSingle(t =>
            t.Direction == TransactionDirection.Credit && t.Amount.Amount == 200m
        );
        operation.DomainEvents.Should().NotContain(e => e is ThirdPartyTransferProcessedEvent);

        result.Value.OperationId.Should().Be(operation.Id);
        result.Value.RequestedAmount.Amount.Should().Be(200m);
        result.Value.AppliedAmount.Amount.Should().Be(200m);
        result.Value.FeeAmount.Amount.Should().Be(0m);
        result.Value.OccurredAt.Should().Be(FixedNow);
    }

    [Theory]
    [InlineData(TransferFlow.CashierThirdParty, FinancialOperationKind.CashierTransfer)]
    [InlineData(TransferFlow.OwnAccounts, FinancialOperationKind.OwnAccountTransfer)]
    [InlineData(TransferFlow.Beneficiary, FinancialOperationKind.BeneficiaryTransfer)]
    [InlineData(TransferFlow.Express, FinancialOperationKind.ExpressTransfer)]
    public async Task TransferAsync_MapsFlowToOperationKind(
        TransferFlow flow,
        FinancialOperationKind expectedKind
    ) {
        SavingsAccount source = Account("100000001", "client-source", 1_000m, id: 1);
        SavingsAccount destination = Account("100000002", "client-dest", 500m, id: 2);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        TransferProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.TransferAsync(
            source,
            destination,
            MoneyOf(200m),
            flow,
            CashierId
        );

        result.IsSuccess.Should().BeTrue();
        addedOperation()!.Kind.Should().Be(expectedKind);
    }

    [Fact]
    public async Task TransferAsync_CashierThirdParty_RaisesThirdPartyTransferProcessedEvent() {
        SavingsAccount source = Account("100000001", "client-source", 1_000m, id: 1);
        SavingsAccount destination = Account("100000002", "client-dest", 500m, id: 2);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        TransferProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.TransferAsync(
            source,
            destination,
            MoneyOf(200m),
            TransferFlow.CashierThirdParty,
            CashierId
        );

        result.IsSuccess.Should().BeTrue();
        addedOperation()!
            .DomainEvents.OfType<ThirdPartyTransferProcessedEvent>()
            .Should()
            .ContainSingle();
    }

    [Theory]
    [InlineData(TransferFlow.OwnAccounts)]
    [InlineData(TransferFlow.Beneficiary)]
    [InlineData(TransferFlow.Express)]
    [InlineData(TransferFlow.CashierThirdParty)]
    public async Task TransferAsync_InsufficientFunds_PersistsRejectedOperation(
        TransferFlow flow
    ) {
        SavingsAccount source = Account("100000001", "client-source", 100m, id: 1);
        SavingsAccount destination = Account("100000002", "client-dest", 500m, id: 2);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        TransferProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.TransferAsync(
            source,
            destination,
            MoneyOf(200m),
            flow,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InsufficientFunds");
        source.Balance.Amount.Should().Be(100m);
        destination.Balance.Amount.Should().Be(500m);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Account.InsufficientFunds");
        operation.RequestedAmount.Amount.Should().Be(200m);
        operation.AppliedAmount.Amount.Should().Be(0m);
        operation.AccountTransactions.Should().ContainSingle(t =>
            t.Direction == TransactionDirection.Debit && t.Amount.Amount == 200m
        );
    }

    [Fact]
    public async Task TransferAsync_InactiveSource_ReturnsNotActiveWithoutPersistence() {
        SavingsAccount source = Account(
            "100000001",
            "client-source",
            1_000m,
            status: AccountStatus.Cancelled,
            id: 1
        );
        SavingsAccount destination = Account("100000002", "client-dest", 500m, id: 2);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        TransferProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.TransferAsync(
            source,
            destination,
            MoneyOf(200m),
            TransferFlow.Beneficiary,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");
        destination.Balance.Amount.Should().Be(500m);
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task TransferAsync_InactiveDestination_ReturnsNotActiveWithoutPersistence() {
        SavingsAccount source = Account("100000001", "client-source", 1_000m, id: 1);
        SavingsAccount destination = Account(
            "100000002",
            "client-dest",
            500m,
            status: AccountStatus.Cancelled,
            id: 2
        );
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        TransferProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.TransferAsync(
            source,
            destination,
            MoneyOf(200m),
            TransferFlow.Beneficiary,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");
        source.Balance.Amount.Should().Be(1_000m);
        Assert.Null(addedOperation());
    }
    [Fact]
    public async Task TransferAsync_UnitOfWorkFails_PropagatesError() {
        SavingsAccount source = Account("100000001", "client-source", 1_000m, id: 1);
        SavingsAccount destination = Account("100000002", "client-dest", 500m, id: 2);
        var operationRepository = OperationRepository(out _);
        var failingUnitOfWork = new Mock<IUnitOfWork>();
        failingUnitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result>>>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Result.Failure(DomainError.Conflict("UnitOfWork.Failed", "fallo de persistencia"))
            );
        var processor = new TransferProcessor(
            new Mock<ISavingsAccountRepository>().Object,
            operationRepository.Object,
            failingUnitOfWork.Object,
            Clock().Object
        );

        Result<FinancialOperationOutcome> result = await processor.TransferAsync(
            source,
            destination,
            MoneyOf(200m),
            TransferFlow.OwnAccounts,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("UnitOfWork.Failed");
        source.Balance.Amount.Should().Be(1_000m);
        destination.Balance.Amount.Should().Be(500m);
    }
}
