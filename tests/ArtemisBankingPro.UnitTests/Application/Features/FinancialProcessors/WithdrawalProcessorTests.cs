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

public sealed class WithdrawalProcessorTests {
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 11, 9, 0, 0, TimeSpan.FromHours(-4));

    private const string CashierId = "cashier-1";

    private static SavingsAccount Account(decimal balance, AccountStatus status = AccountStatus.Active) {
        if (status == AccountStatus.Cancelled) {
            var cancelled = SavingsAccount
                .OpenSecondary(
                    "client-1",
                    AccountNumber.Create("100000001").Value,
                    Money.Zero,
                    CashierId,
                    FixedNow
                )
                .Value;
            cancelled.Cancel(FixedNow).IsSuccess.Should().BeTrue();
            return cancelled;
        }

        return SavingsAccount
            .OpenPrimary(
                "client-1",
                AccountNumber.Create("100000001").Value,
                Money.Create(balance).Value,
                CashierId,
                FixedNow
            )
            .Value;
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

    private static WithdrawalProcessor CreateProcessor(
        Mock<IFinancialOperationRepository> operationRepository
    ) =>
        new(
            new Mock<ISavingsAccountRepository>().Object,
            operationRepository.Object,
            UnitOfWork().Object,
            Clock().Object
        );

    [Fact]
    public async Task WithdrawAsync_Success_DebitsAccountAndCreatesApprovedOperation() {
        SavingsAccount account = Account(50_000m);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        WithdrawalProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.WithdrawAsync(
            account,
            Money.Create(1000m).Value,
            CashierId
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.RequestedAmount.Amount.Should().Be(1000m);
        result.Value.AppliedAmount.Amount.Should().Be(1000m);
        account.Balance.Amount.Should().Be(49_000m);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.Kind.Should().Be(FinancialOperationKind.Withdrawal);
        operation.Status.Should().Be(FinancialOperationStatus.Approved);
        operation.InitiatedByUserId.Should().Be(CashierId);
        operation.AccountTransactions.Should().ContainSingle(t =>
            t.Direction == TransactionDirection.Debit
            && t.AccountNumber.Value == "100000001"
            && t.OriginReference == "100000001"
            && t.BeneficiaryReference == "RETIRO"
            && t.Amount.Amount == 1000m
        );
        operation
            .DomainEvents.OfType<WithdrawalProcessedEvent>()
            .Should()
            .ContainSingle(ev => ev.Amount.Amount == 1000m);
    }

    [Fact]
    public async Task WithdrawAsync_InsufficientFunds_PersistsRejectedOperationWithoutBalanceChange() {
        SavingsAccount account = Account(1000m);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        WithdrawalProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.WithdrawAsync(
            account,
            Money.Create(5000m).Value,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InsufficientFunds");
        account.Balance.Amount.Should().Be(1000m);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Account.InsufficientFunds");
        operation.RequestedAmount.Amount.Should().Be(5000m);
        operation.AppliedAmount.Amount.Should().Be(0m);
        operation.AccountTransactions.Should().ContainSingle(t =>
            t.Direction == TransactionDirection.Debit
            && t.OriginReference == "100000001"
            && t.BeneficiaryReference == "RETIRO"
            && t.Amount.Amount == 5000m
        );
        operation.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task WithdrawAsync_InactiveAccount_ReturnsNotActiveWithoutPersistence() {
        SavingsAccount account = Account(1000m, AccountStatus.Cancelled);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        WithdrawalProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.WithdrawAsync(
            account,
            Money.Create(500m).Value,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");
        Assert.Null(addedOperation());
    }
}
