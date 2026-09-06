using System.Data;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Moq;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.UnitTests.Application.Features.FinancialProcessors;

public sealed class CardPaymentProcessorTests {
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 11, 9, 0, 0, TimeSpan.FromHours(-4));

    private static readonly DateOnly FixedToday = new(2026, 8, 11);

    private const string CashierId = "cashier-1";
    private const string ClientId = "client-1";

    private static CreditCardEntity Card(decimal debt = 4000m) {
        var card = CreditCardEntity.Issue(
            ClientId,
            "1234",
            new string('a', 64),
            CvcDigest.Create(new string('c', 64)).Value,
            Money.Create(10000m).Value,
            "admin-1",
            FixedNow,
            FixedToday
        ).Value;
        typeof(CreditCardEntity)
            .GetProperty(nameof(CreditCardEntity.Id))!
            .GetSetMethod(true)!
            .Invoke(card, [1]);
        if (debt > 0m) {
            card.AuthorizeCharge(Money.Create(debt).Value, FixedToday)
                .IsSuccess
                .Should()
                .BeTrue();
        }

        return card;
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

    private static CardPaymentProcessor CreateProcessor(
        Mock<IFinancialOperationRepository> operationRepository
    ) =>
        new(
            new Mock<ICreditCardRepository>().Object,
            new Mock<ISavingsAccountRepository>().Object,
            operationRepository.Object,
            UnitOfWork().Object,
            Clock().Object
        );

    [Fact]
    public async Task PayAsync_Success_CapsToDebtDebitsAndReducesDebt() {
        CreditCardEntity card = Card();
        SavingsAccount account = Account(50_000m);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        CardPaymentProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.PayAsync(
            card,
            account,
            Money.Create(2000m).Value,
            CashierId
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.RequestedAmount.Amount.Should().Be(2000m);
        result.Value.AppliedAmount.Amount.Should().Be(2000m);
        account.Balance.Amount.Should().Be(48_000m);
        card.CurrentDebt.Amount.Should().Be(2000m);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.Kind.Should().Be(FinancialOperationKind.CreditCardPayment);
        operation.Status.Should().Be(FinancialOperationStatus.Approved);
        operation.CreditCardId.Should().Be(card.Id);
        operation.AccountTransactions.Should().ContainSingle(t =>
            t.Direction == TransactionDirection.Debit
            && t.OriginReference == "100000001"
            && t.BeneficiaryReference == "1234"
            && t.Amount.Amount == 2000m
        );
    }

    [Fact]
    public async Task PayAsync_Overpayment_CapsToOutstandingDebt() {
        CreditCardEntity card = Card();
        SavingsAccount account = Account(50_000m);
        var operationRepository = OperationRepository(out _);
        CardPaymentProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.PayAsync(
            card,
            account,
            Money.Create(9000m).Value,
            CashierId
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.AppliedAmount.Amount.Should().Be(4000m);
        card.CurrentDebt.Amount.Should().Be(0m);
        account.Balance.Amount.Should().Be(46_000m);
    }

    [Fact]
    public async Task PayAsync_InsufficientFunds_PersistsRejectedOperationWithoutStateChanges() {
        CreditCardEntity card = Card();
        SavingsAccount account = Account(100m);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        CardPaymentProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.PayAsync(
            card,
            account,
            Money.Create(200m).Value,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InsufficientFunds");
        account.Balance.Amount.Should().Be(100m);
        card.CurrentDebt.Amount.Should().Be(4000m);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Account.InsufficientFunds");
        operation.CreditCardId.Should().Be(card.Id);
        operation.AccountTransactions.Should().ContainSingle(t =>
            t.Direction == TransactionDirection.Debit
            && t.OriginReference == "100000001"
            && t.BeneficiaryReference == "1234"
            && t.Amount.Amount == 200m
        );
    }

    [Fact]
    public async Task PayAsync_NoDebt_RecordsRejectedOperationWithoutStateChanges() {
        CreditCardEntity card = Card(debt: 0m);
        SavingsAccount account = Account(50_000m);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        CardPaymentProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.PayAsync(
            card,
            account,
            Money.Create(500m).Value,
            CashierId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Card.NoDebt");
        FinancialOperation operation = addedOperation()!;
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Card.NoDebt");
    }
}
