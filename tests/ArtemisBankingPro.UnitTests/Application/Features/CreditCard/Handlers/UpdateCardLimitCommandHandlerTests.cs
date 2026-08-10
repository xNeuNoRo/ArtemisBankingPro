using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.CreditCard.Handlers;

public sealed class UpdateCardLimitCommandHandlerTests {
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(-4));
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

    private static Mock<IUserRepository> UserRepository() {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(r => r.GetByIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new UserListDto(
                    "client-1",
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
    public async Task Handle_ActiveCard_UpdatesLimitRecordsOperationAndSendsEmail() {
        CreditCardEntity card = SeedCard();
        card.AuthorizeCharge(Money.Create(500m).Value, FixedToday).IsSuccess.Should().BeTrue();
        var cardRepository = CardRepository(card);
        var operationRepository = FinancialOperationRepository(
            out Func<FinancialOperation?> added
        );
        var emailService = new Mock<IEmailService>();
        var handler = new UpdateCardLimitCommandHandler(
            cardRepository.Object,
            operationRepository.Object,
            UserRepository().Object,
            UnitOfWork().Object,
            CurrentUser().Object,
            Clock().Object,
            emailService.Object,
            NullLogger<UpdateCardLimitCommandHandler>.Instance
        );

        var result = await handler.Handle(
            new UpdateCardLimitCommand(1, 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        card.CreditLimit.Amount.Should().Be(15_000m);
        card.AvailableCredit.Amount.Should().Be(14_500m);

        cardRepository.Verify(r => r.Update(card), Times.Once);

        added().Should().NotBeNull();
        added()!.Kind.Should().Be(FinancialOperationKind.CardLimitChanged);
        added()!.RequestedAmount.Should().Be(Money.Zero);
        added()!.AppliedAmount.Should().Be(Money.Zero);
        added()!.CreditCardId.Should().Be(1);
        added()!.InitiatedByUserId.Should().Be("admin-1");
        added()!.AccountTransactions.Should().BeEmpty();

        emailService.Verify(
            service => service.SendAsync(
                "maria@artemis.com",
                It.Is<CardLimitChangedModel>(model =>
                    model.LastFour == "1234"
                    && model.NewLimit.Amount == 15_000m
                    && model.CustomerName == "María Gómez"
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_UnknownCard_ReturnsCardNotFoundWithoutPersistence() {
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreditCardEntity?)null);
        var operationRepository = FinancialOperationRepository(out Func<FinancialOperation?> added);
        var handler = new UpdateCardLimitCommandHandler(
            cardRepository.Object,
            operationRepository.Object,
            UserRepository().Object,
            UnitOfWork().Object,
            CurrentUser().Object,
            Clock().Object,
            new Mock<IEmailService>().Object,
            NullLogger<UpdateCardLimitCommandHandler>.Instance
        );

        var result = await handler.Handle(
            new UpdateCardLimitCommand(99, 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.NotFound");
        result.Error.Category.Should().Be(ErrorCategory.NotFound);
        cardRepository.Verify(r => r.Update(It.IsAny<CreditCardEntity>()), Times.Never);
        added().Should().BeNull();
    }

    [Fact]
    public async Task Handle_LimitBelowDebt_ReturnsConflictWithoutPersistence() {
        CreditCardEntity card = SeedCard();
        card.AuthorizeCharge(Money.Create(5_000m).Value, FixedToday).IsSuccess.Should().BeTrue();
        var cardRepository = CardRepository(card);
        var operationRepository = FinancialOperationRepository(out Func<FinancialOperation?> added);
        var handler = new UpdateCardLimitCommandHandler(
            cardRepository.Object,
            operationRepository.Object,
            UserRepository().Object,
            UnitOfWork().Object,
            CurrentUser().Object,
            Clock().Object,
            new Mock<IEmailService>().Object,
            NullLogger<UpdateCardLimitCommandHandler>.Instance
        );

        var result = await handler.Handle(
            new UpdateCardLimitCommand(1, 4_999.99m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.LimitBelowDebt");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
        card.CreditLimit.Amount.Should().Be(10_000m);
        cardRepository.Verify(r => r.Update(It.IsAny<CreditCardEntity>()), Times.Never);
        added().Should().BeNull();
    }

    [Fact]
    public async Task Handle_CancelledCard_ReturnsNotActiveWithoutPersistence() {
        CreditCardEntity card = SeedCard();
        card.Cancel(FixedNow).IsSuccess.Should().BeTrue();
        var cardRepository = CardRepository(card);
        var operationRepository = FinancialOperationRepository(out Func<FinancialOperation?> added);
        var handler = new UpdateCardLimitCommandHandler(
            cardRepository.Object,
            operationRepository.Object,
            UserRepository().Object,
            UnitOfWork().Object,
            CurrentUser().Object,
            Clock().Object,
            new Mock<IEmailService>().Object,
            NullLogger<UpdateCardLimitCommandHandler>.Instance
        );

        var result = await handler.Handle(
            new UpdateCardLimitCommand(1, 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.NotActive");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
        card.CreditLimit.Amount.Should().Be(10_000m);
        cardRepository.Verify(r => r.Update(It.IsAny<CreditCardEntity>()), Times.Never);
        added().Should().BeNull();
    }

    [Fact]
    public async Task Handle_NonPositiveLimit_ReturnsValidationFailureWithoutPersistence() {
        CreditCardEntity card = SeedCard();
        var cardRepository = CardRepository(card);
        var operationRepository = FinancialOperationRepository(out Func<FinancialOperation?> added);
        var handler = new UpdateCardLimitCommandHandler(
            cardRepository.Object,
            operationRepository.Object,
            UserRepository().Object,
            UnitOfWork().Object,
            CurrentUser().Object,
            Clock().Object,
            new Mock<IEmailService>().Object,
            NullLogger<UpdateCardLimitCommandHandler>.Instance
        );

        var result = await handler.Handle(
            new UpdateCardLimitCommand(1, 0m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.LimitMustBePositive");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
        card.CreditLimit.Amount.Should().Be(10_000m);
        cardRepository.Verify(r => r.Update(It.IsAny<CreditCardEntity>()), Times.Never);
        added().Should().BeNull();
    }

    [Fact]
    public async Task Handle_EmailFailure_KeepsOperationSuccessful() {
        CreditCardEntity card = SeedCard();
        var cardRepository = CardRepository(card);
        var operationRepository = FinancialOperationRepository(out Func<FinancialOperation?> added);
        var emailService = new Mock<IEmailService>();
        emailService
            .Setup(service => service.SendAsync(
                It.IsAny<string>(),
                It.IsAny<CardLimitChangedModel>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(
                new EmailSendException(
                    "Modificación de límite de tarjeta",
                    new InvalidOperationException("Fallo simulado del proveedor.")
                )
            );
        var handler = new UpdateCardLimitCommandHandler(
            cardRepository.Object,
            operationRepository.Object,
            UserRepository().Object,
            UnitOfWork().Object,
            CurrentUser().Object,
            Clock().Object,
            emailService.Object,
            NullLogger<UpdateCardLimitCommandHandler>.Instance
        );

        var result = await handler.Handle(
            new UpdateCardLimitCommand(1, 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        card.CreditLimit.Amount.Should().Be(15_000m);
        cardRepository.Verify(r => r.Update(card), Times.Once);
        added().Should().NotBeNull();
    }

    [Fact]
    public void Command_ImplementsIdempotencyWithKeyIncludingCardAndLimit() {
        var command = new UpdateCardLimitCommand(42, 15_000m);

        command.IdempotencyKey.Should().Be("update-card-limit-42-15000");
        command.RequestFingerprint.Should().Be("42|15000");
    }

    [Fact]
    public void Command_RequiresAdministratorRole() {
        var command = new UpdateCardLimitCommand(1, 15_000m);

        command.RequiredRoles.Should().BeEquivalentTo("Administrador");
    }
}
