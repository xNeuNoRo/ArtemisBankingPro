using System.Data;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Moq;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.UnitTests.Application.Features.FinancialProcessors;

public sealed class CashAdvanceProcessorTests {
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 11, 9, 0, 0, TimeSpan.FromHours(-4));

    private static readonly DateOnly FixedToday = new(2026, 8, 11);

    private const string ClientId = "client-1";
    private const string AdminId = "admin-1";

    private static void SetId(CreditCardEntity card, int id) =>
        typeof(CreditCardEntity)
            .GetProperty(nameof(CreditCardEntity.Id))!
            .GetSetMethod(true)!
            .Invoke(card, [id]);

    private static void SetId(SavingsAccount account, int id) =>
        typeof(SavingsAccount)
            .GetProperty(nameof(SavingsAccount.Id))!
            .GetSetMethod(true)!
            .Invoke(account, [id]);

    private static CreditCardEntity Card(decimal limit = 10_000m, decimal debt = 0m) {
        CreditCardEntity card = CreditCardEntity.Issue(
            ClientId,
            "1234",
            new string('a', 64),
            CvcDigest.Create(new string('c', 64)).Value,
            Money.Create(limit).Value,
            AdminId,
            FixedNow,
            FixedToday
        ).Value;
        SetId(card, 1);
        if (debt > 0m) {
            card.AuthorizeCharge(Money.Create(debt).Value, FixedToday)
                .IsSuccess.Should().BeTrue();
        }

        return card;
    }

    private static CreditCardEntity ExpiredCard(decimal limit = 10_000m) {
        CreditCardEntity card = Card(limit);
        typeof(CreditCardEntity)
            .GetProperty(nameof(CreditCardEntity.Expiration))!
            .GetSetMethod(true)!
            .Invoke(card, [CardExpiration.Create(8, 2025).Value]);
        return card;
    }

    private static SavingsAccount Account(
        string number,
        decimal balance,
        AccountStatus status = AccountStatus.Active,
        int id = 2
    ) {
        SavingsAccount account =
            status == AccountStatus.Cancelled
                ? OpenCancelledAccount(number)
                : SavingsAccount
                    .OpenPrimary(
                        ClientId,
                        AccountNumber.Create(number).Value,
                        Money.Create(balance).Value,
                        AdminId,
                        FixedNow
                    )
                    .Value;
        SetId(account, id);
        return account;
    }

    private static SavingsAccount OpenCancelledAccount(string number) {
        var account = SavingsAccount
            .OpenSecondary(
                ClientId,
                AccountNumber.Create(number).Value,
                Money.Zero,
                AdminId,
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
        clock.SetupGet(c => c.Today).Returns(FixedToday);
        return clock;
    }

    private static CashAdvanceProcessor CreateProcessor(
        Mock<IFinancialOperationRepository>? operationRepository = null,
        Mock<ICreditCardRepository>? creditCardRepository = null,
        Mock<ISavingsAccountRepository>? accountRepository = null
    ) {
        var operationRepositoryMock = operationRepository ?? OperationRepository(out _);
        var creditCardRepositoryMock = creditCardRepository ?? new Mock<ICreditCardRepository>();
        var accountRepositoryMock = accountRepository ?? new Mock<ISavingsAccountRepository>();
        var unitOfWork = UnitOfWork();
        var clock = Clock();
        return new CashAdvanceProcessor(
            creditCardRepositoryMock.Object,
            accountRepositoryMock.Object,
            operationRepositoryMock.Object,
            unitOfWork.Object,
            clock.Object
        );
    }

    [Fact]
    public async Task AdvanceAsync_Success_ChargesCardAndCreditsAccount() {
        CreditCardEntity card = Card(limit: 10_000m);
        SavingsAccount destination = Account("100000001", balance: 500m);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        CashAdvanceProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.AdvanceAsync(
            card,
            destination,
            Money.Create(100m).Value,
            ClientId
        );

        result.IsSuccess.Should().BeTrue();
        card.CurrentDebt.Amount.Should().Be(106.25m);
        destination.Balance.Amount.Should().Be(600m);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.Status.Should().Be(FinancialOperationStatus.Approved);
        operation.Kind.Should().Be(FinancialOperationKind.CashAdvance);
        operation.RequestedAmount.Amount.Should().Be(100m);
        operation.AppliedAmount.Amount.Should().Be(100m);
        operation.InterestAmount.Amount.Should().Be(6.25m);
        operation.CreditCardId.Should().Be(card.Id);
        Assert.NotNull(operation.CardConsumption);
        operation.CardConsumption.Amount.Amount.Should().Be(106.25m);
        operation.CardConsumption.MerchantDisplayName.Should().Be("AVANCE");
        operation.CardConsumption.Type.Should().Be(ConsumptionType.CashAdvance);

        result.Value.OperationId.Should().Be(operation.Id);
        result.Value.RequestedAmount.Amount.Should().Be(100m);
        result.Value.AppliedAmount.Amount.Should().Be(100m);
        result.Value.FeeAmount.Amount.Should().Be(6.25m);
        result.Value.OccurredAt.Should().Be(FixedNow);
    }

    [Fact]
    public async Task AdvanceAsync_InsufficientCredit_PersistsRejectedOperationWithoutStateChanges() {
        CreditCardEntity card = Card(limit: 100m);
        SavingsAccount destination = Account("100000001", balance: 500m);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        CashAdvanceProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.AdvanceAsync(
            card,
            destination,
            Money.Create(100m).Value,
            ClientId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Card.InsufficientCredit");
        card.CurrentDebt.Amount.Should().Be(0m);
        destination.Balance.Amount.Should().Be(500m);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Card.InsufficientCredit");
        operation.Kind.Should().Be(FinancialOperationKind.CashAdvance);
        operation.RequestedAmount.Amount.Should().Be(100m);
        operation.AppliedAmount.Amount.Should().Be(0m);
        operation.InterestAmount.Amount.Should().Be(6.25m);
        operation.CreditCardId.Should().Be(card.Id);
        Assert.NotNull(operation.CardConsumption);
        operation.CardConsumption.Amount.Amount.Should().Be(106.25m);
    }

    [Fact]
    public async Task AdvanceAsync_InactiveDestination_ReturnsNotActiveWithoutPersistence() {
        CreditCardEntity card = Card();
        SavingsAccount destination = Account(
            "100000001",
            balance: 0m,
            status: AccountStatus.Cancelled
        );
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        CashAdvanceProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.AdvanceAsync(
            card,
            destination,
            Money.Create(100m).Value,
            ClientId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");
        card.CurrentDebt.Amount.Should().Be(0m);
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task AdvanceAsync_ExpiredCard_ReturnsExpiredWithoutPersistingRejection() {
        CreditCardEntity card = ExpiredCard();
        SavingsAccount destination = Account("100000001", balance: 500m);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        CashAdvanceProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.AdvanceAsync(
            card,
            destination,
            Money.Create(100m).Value,
            ClientId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Card.Expired");
        card.CurrentDebt.Amount.Should().Be(0m);
        destination.Balance.Amount.Should().Be(500m);
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task AdvanceAsync_TinyPrincipal_WithZeroRoundedInterest_IsApproved() {
        CreditCardEntity card = Card(limit: 10_000m);
        SavingsAccount destination = Account("100000001", balance: 500m);
        var operationRepository = OperationRepository(out Func<FinancialOperation?> addedOperation);
        CashAdvanceProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.AdvanceAsync(
            card,
            destination,
            Money.Create(0.01m).Value,
            ClientId
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.FeeAmount.Amount.Should().Be(0m);
        card.CurrentDebt.Amount.Should().Be(0.01m);
        destination.Balance.Amount.Should().Be(500.01m);

        FinancialOperation operation = addedOperation()!;
        Assert.NotNull(operation);
        operation.InterestAmount.Amount.Should().Be(0m);
        operation.CardConsumption!.Amount.Amount.Should().Be(0.01m);
    }
    [Fact]
    public async Task AdvanceAsync_NonPositivePrincipal_ReturnsError() {
        SavingsAccount account = Account("100000001", 10_000m);
        CreditCardEntity card = Card();
        var operationRepository = OperationRepository(out _);
        CashAdvanceProcessor processor = CreateProcessor(operationRepository);

        Result<FinancialOperationOutcome> result = await processor.AdvanceAsync(
            card,
            account,
            Money.Zero,
            ClientId
        );

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AdvanceAsync_UnitOfWorkFails_PropagatesError() {
        SavingsAccount account = Account("100000001", 10_000m);
        CreditCardEntity card = Card();
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
        var processor = new CashAdvanceProcessor(
            new Mock<ICreditCardRepository>().Object,
            new Mock<ISavingsAccountRepository>().Object,
            operationRepository.Object,
            failingUnitOfWork.Object,
            Clock().Object
        );

        Result<FinancialOperationOutcome> result = await processor.AdvanceAsync(
            card,
            account,
            Money.Create(100m).Value,
            ClientId
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("UnitOfWork.Failed");
    }
}
