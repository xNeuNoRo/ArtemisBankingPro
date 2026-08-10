using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.CreditCard.Handlers;

public sealed class CancelCreditCardCommandHandlerTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateOnly FixedToday = new(2026, 8, 10);

    private static CreditCardEntity SeedCard() =>
        CreditCardEntity.Issue(
            "client-1",
            "1234",
            new string('a', 64),
            CvcDigest.Create(new string('c', 64)).Value,
            Money.Create(10_000m).Value,
            "admin-1",
            FixedNow,
            FixedToday).Value;

    private static Mock<ICreditCardRepository> CardRepository(CreditCardEntity card) {
        var repository = new Mock<ICreditCardRepository>();
        repository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        return repository;
    }

    private static Mock<IFinancialOperationRepository> FinancialOperationRepository(
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
                It.IsAny<System.Data.IsolationLevel>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                (Func<CancellationToken, Task<Result>> operation,
                    System.Data.IsolationLevel _,
                    CancellationToken ct) => operation(ct)
            );
        return uow;
    }

    private static Mock<IBusinessClock> Clock() {
        var clock = new Mock<IBusinessClock>();
        clock.SetupGet(c => c.Now).Returns(FixedNow);
        clock.SetupGet(c => c.NowUtc).Returns(FixedNow);
        clock.SetupGet(c => c.Today).Returns(FixedToday);
        return clock;
    }

    private static Mock<ICurrentUserService> CurrentUser() {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.UserId).Returns("admin-1");
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        return user;
    }

    [Fact]
    public async Task Handle_ActiveCardWithoutDebt_CancelsAndRecordsOperation() {
        CreditCardEntity card = SeedCard();
        var cardRepository = CardRepository(card);
        var operationRepository = FinancialOperationRepository(out Func<FinancialOperation?> added);
        var handler = new CancelCreditCardCommandHandler(
            cardRepository.Object,
            operationRepository.Object,
            UnitOfWork().Object,
            CurrentUser().Object,
            Clock().Object
        );

        var result = await handler.Handle(new CancelCreditCardCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        card.Status.Should().Be(CreditCardStatus.Cancelled);
        card.CancelledAt.Should().Be(FixedNow);

        cardRepository.Verify(r => r.Update(card), Times.Once);

        added().Should().NotBeNull();
        added()!.Kind.Should().Be(FinancialOperationKind.CardCancelled);
        added()!.Status.Should().Be(FinancialOperationStatus.Approved);
        added()!.RequestedAmount.Should().Be(Money.Zero);
        added()!.AppliedAmount.Should().Be(Money.Zero);
        added()!.InterestAmount.Should().Be(Money.Zero);
        added()!.CreditCardId.Should().Be(1);
        added()!.InitiatedByUserId.Should().Be("admin-1");
        added()!.OccurredAt.Should().Be(FixedNow);
        added()!.AccountTransactions.Should().BeEmpty();
        added()!.CardConsumption.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UnknownCard_ReturnsCardNotFoundWithoutPersistence() {
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreditCardEntity?)null);
        var operationRepository = FinancialOperationRepository(out Func<FinancialOperation?> added);
        var handler = new CancelCreditCardCommandHandler(
            cardRepository.Object,
            operationRepository.Object,
            UnitOfWork().Object,
            CurrentUser().Object,
            Clock().Object
        );

        var result = await handler.Handle(new CancelCreditCardCommand(99), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.NotFound");
        result.Error.Category.Should().Be(ErrorCategory.NotFound);
        cardRepository.Verify(r => r.Update(It.IsAny<CreditCardEntity>()), Times.Never);
        added().Should().BeNull();
    }

    [Fact]
    public async Task Handle_CardWithDebt_ReturnsConflictWithoutPersistence() {
        CreditCardEntity card = SeedCard();
        card.AuthorizeCharge(Money.Create(100m).Value, FixedToday).IsSuccess.Should().BeTrue();
        var cardRepository = CardRepository(card);
        var operationRepository = FinancialOperationRepository(out Func<FinancialOperation?> added);
        var handler = new CancelCreditCardCommandHandler(
            cardRepository.Object,
            operationRepository.Object,
            UnitOfWork().Object,
            CurrentUser().Object,
            Clock().Object
        );

        var result = await handler.Handle(new CancelCreditCardCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.DebtMustBeZero");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
        card.Status.Should().Be(CreditCardStatus.Active);
        cardRepository.Verify(r => r.Update(It.IsAny<CreditCardEntity>()), Times.Never);
        added().Should().BeNull();
    }

    [Fact]
    public async Task Handle_AlreadyCancelledCard_ReturnsConflictWithoutPersistence() {
        CreditCardEntity card = SeedCard();
        card.Cancel(FixedNow).IsSuccess.Should().BeTrue();
        var cardRepository = CardRepository(card);
        var operationRepository = FinancialOperationRepository(out Func<FinancialOperation?> added);
        var handler = new CancelCreditCardCommandHandler(
            cardRepository.Object,
            operationRepository.Object,
            UnitOfWork().Object,
            CurrentUser().Object,
            Clock().Object
        );

        var result = await handler.Handle(new CancelCreditCardCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.NotActive");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
        cardRepository.Verify(r => r.Update(It.IsAny<CreditCardEntity>()), Times.Never);
        added().Should().BeNull();
    }

    [Fact]
    public async Task Handle_OperationPersistenceFailure_DoesNotReportSuccess() {
        CreditCardEntity card = SeedCard();
        var cardRepository = CardRepository(card);
        var operationRepository = FinancialOperationRepository(out _);
        var unitOfWork = UnitOfWork();
        unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result>>>(),
                It.IsAny<System.Data.IsolationLevel>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(
                Result.Failure(
                    DomainError.Conflict("Card.PersistenceFailed", "No se pudo persistir la cancelación.")
                )
            );
        var handler = new CancelCreditCardCommandHandler(
            cardRepository.Object,
            operationRepository.Object,
            unitOfWork.Object,
            CurrentUser().Object,
            Clock().Object
        );

        var result = await handler.Handle(new CancelCreditCardCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.PersistenceFailed");
    }

    [Fact]
    public void Command_ImplementsIdempotencyWithStableKeyAndFingerprint() {
        var command = new CancelCreditCardCommand(42);

        command.IdempotencyKey.Should().Be("cancel-card-42");
        command.RequestFingerprint.Should().Be("42");
    }

    [Fact]
    public void Command_RequiresAdministratorRole() {
        var command = new CancelCreditCardCommand(1);

        command.RequiredRoles.Should().BeEquivalentTo("Administrador");
    }

    [Fact]
    public async Task Command_RepeatedExecution_AppliesEffectOnlyOnce() {
        CreditCardEntity card = SeedCard();
        var cardRepository = CardRepository(card);
        var operationRepository = FinancialOperationRepository(out _);
        var handler = new CancelCreditCardCommandHandler(
            cardRepository.Object,
            operationRepository.Object,
            UnitOfWork().Object,
            CurrentUser().Object,
            Clock().Object
        );

        var first = await handler.Handle(new CancelCreditCardCommand(1), CancellationToken.None);
        var second = await handler.Handle(new CancelCreditCardCommand(1), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsFailure.Should().BeTrue();
        second.Error!.Code.Should().Be("Card.NotActive");
        cardRepository.Verify(r => r.Update(It.IsAny<CreditCardEntity>()), Times.Once);
        operationRepository.Verify(
            r => r.AddAsync(It.IsAny<FinancialOperation>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }
}
