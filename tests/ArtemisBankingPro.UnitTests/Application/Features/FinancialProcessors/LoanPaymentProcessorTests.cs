using System.Data;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.FinancialProcessors;

public sealed class LoanPaymentProcessorTests {
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 11, 9, 0, 0, TimeSpan.FromHours(-4));

    private static readonly DateOnly FixedToday = new(2026, 8, 11);

    private const string CashierId = "cashier-1";
    private const string ClientId = "client-1";

    private static Loan SeedLoan(decimal principal = 12000m) =>
        Loan.Issue(
            ClientId,
            LoanNumber.Create("111111111").Value,
            Money.Create(principal).Value,
            12,
            InterestRate.Create(12m).Value,
            "admin-1",
            FixedNow,
            FixedToday
        ).Value;

    private static Loan PaidOffLoan() {
        Loan loan = SeedLoan();
        loan.ApplyPayment(loan.OutstandingAmount, FixedNow)
            .IsSuccess
            .Should()
            .BeTrue();
        return loan;
    }

    private static SavingsAccount Account(decimal balance) =>
        SavingsAccount
            .OpenPrimary(
                ClientId,
                AccountNumber.Create("100000001").Value,
                Money.Create(balance).Value,
                CashierId,
                FixedNow
            )
            .Value;

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

    private static LoanPaymentProcessor CreateProcessor(
        Mock<IFinancialOperationRepository> operationRepository
    ) =>
        new(
            new Mock<ILoanRepository>().Object,
            new Mock<ISavingsAccountRepository>().Object,
            operationRepository.Object,
            UnitOfWork().Object,
            Clock().Object
        );

    [Fact]
    public async Task PayAsync_Success_DebitsAppliesPaymentAndCreatesOperation() {
        Loan loan = SeedLoan();
        SavingsAccount account = Account(50_000m);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        LoanPaymentProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.PayAsync(
            loan,
            account,
            Money.Create(2000m).Value,
            CashierId
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.RequestedAmount.Amount.Should().Be(2000m);
        result.Value.AppliedAmount.Amount.Should().Be(2000m);
        account.Balance.Amount.Should().Be(48_000m);
        loan.OutstandingAmount.Amount.Should().BeLessThan(12000m);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.Kind.Should().Be(FinancialOperationKind.LoanPayment);
        operation.Status.Should().Be(FinancialOperationStatus.Approved);
        operation.LoanNumber!.Value.Should().Be("111111111");
        operation.AccountTransactions.Should().ContainSingle(t =>
            t.Direction == TransactionDirection.Debit
            && t.OriginReference == "100000001"
            && t.BeneficiaryReference == "111111111"
            && t.Amount.Amount == 2000m
        );
    }

    [Fact]
    public async Task PayAsync_InsufficientFunds_PersistsRejectedOperationWithoutStateChanges() {
        Loan loan = SeedLoan();
        SavingsAccount account = Account(100m);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        LoanPaymentProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.PayAsync(
            loan,
            account,
            Money.Create(200m).Value,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InsufficientFunds");
        account.Balance.Amount.Should().Be(100m);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Account.InsufficientFunds");
        operation.LoanNumber!.Value.Should().Be("111111111");
        operation.AccountTransactions.Should().ContainSingle(t =>
            t.Direction == TransactionDirection.Debit
            && t.OriginReference == "100000001"
            && t.BeneficiaryReference == "111111111"
            && t.Amount.Amount == 200m
        );
    }

    [Fact]
    public async Task PayAsync_CompletedLoan_ReturnsNotActiveWithoutPersistence() {
        Loan loan = PaidOffLoan();
        SavingsAccount account = Account(50_000m);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        LoanPaymentProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.PayAsync(
            loan,
            account,
            Money.Create(500m).Value,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Loan.NotActive");
        Assert.Null(addedOperation());
    }
    [Fact]
    public async Task PayAsync_CancelledAccount_ReturnsNotActiveWithoutPersistence() {
        Loan loan = SeedLoan();
        var cancelled = SavingsAccount
            .OpenSecondary(
                ClientId,
                AccountNumber.Create("100000002").Value,
                Money.Zero,
                CashierId,
                FixedNow)
            .Value;
        cancelled.Cancel(FixedNow).IsSuccess.Should().BeTrue();
        var operationRepository = OperationRepository(out _);
        LoanPaymentProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.PayAsync(
            loan,
            cancelled,
            Money.Create(200m).Value,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");
    }

    [Fact]
    public async Task PayAsync_UnitOfWorkFails_PropagatesError() {
        Loan loan = SeedLoan();
        SavingsAccount account = Account(50_000m);
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
        var processor = new LoanPaymentProcessor(
            new Mock<ILoanRepository>().Object,
            new Mock<ISavingsAccountRepository>().Object,
            operationRepository.Object,
            failingUnitOfWork.Object,
            Clock().Object
        );

        Result<FinancialOperationOutcome> result = await processor.PayAsync(
            loan,
            account,
            Money.Create(200m).Value,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("UnitOfWork.Failed");
    }

    [Fact]
    public async Task PayAsync_FailedRejectionPersistence_PropagatesError() {
        Loan loan = SeedLoan();
        SavingsAccount account = Account(100m);
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
        var processor = new LoanPaymentProcessor(
            new Mock<ILoanRepository>().Object,
            new Mock<ISavingsAccountRepository>().Object,
            operationRepository.Object,
            failingUnitOfWork.Object,
            Clock().Object
        );

        Result<FinancialOperationOutcome> result = await processor.PayAsync(
            loan,
            account,
            Money.Create(200m).Value,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("UnitOfWork.Failed");
    }
}
