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
using ArtemisBankingPro.Domain.Cards.Events;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Cashier.Handlers;

public sealed class ProcessCardPaymentCommandHandlerTests {
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

    private static void SetId(CreditCardEntity card, int id) =>
        typeof(CreditCardEntity)
            .GetProperty(nameof(CreditCardEntity.Id))!
            .GetSetMethod(true)!
            .Invoke(card, [id]);

    private static CreditCardEntity SeedCard(decimal debt = 4000m) {
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
        SetId(card, 1);

        if (debt > 0m) {
            card.AuthorizeCharge(Money.Create(debt).Value, FixedToday)
                .IsSuccess
                .Should()
                .BeTrue();
        }

        return card;
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

    private static ProcessCardPaymentCommandHandler CreateHandler(
        out Mock<ICreditCardRepository> cardRepository,
        out Mock<ISavingsAccountRepository> accountRepository,
        out Mock<IFinancialOperationRepository> operationRepository,
        out Mock<IEmailService> emailService,
        CreditCardEntity? card,
        SavingsAccount? account,
        Mock<IUserRepository>? userRepository = null,
        string accountOwnerUserId = ClientId
    ) {
        cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

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

        var unitOfWork = UnitOfWork();
        var clock = Clock();
        var processor = new CardPaymentProcessor(
            cardRepository.Object,
            accountRepository.Object,
            operationRepository.Object,
            unitOfWork.Object,
            clock.Object
        );

        return new ProcessCardPaymentCommandHandler(
            processor,
            cardRepository.Object,
            accountRepository.Object,
            userRepository.Object,
            clock.Object,
            new FixedCurrentUser(),
            emailService.Object,
            NullLogger<ProcessCardPaymentCommandHandler>.Instance
        );
    }

    private static ProcessCardPaymentCommand Command(decimal amount = 1000m) =>
        new(1, "100000001", amount, "test-key");

    [Fact]
    public async Task Handle_ValidPayment_DebitsAccountReducesDebtAndCreatesOperation() {
        var card = SeedCard();
        var account = SeedAccount();
        var handler = CreateHandler(
            out var cardRepository,
            out var accountRepository,
            out var operationRepository,
            out var emailService,
            card,
            account
        );

        var result = await handler.Handle(Command(1000m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(1000m);
        result.Value.Status.Should().Be("Approved");
        result.Value.CardLastFour.Should().Be("1234");
        result.Value.LoanNumber.Should().BeNull();
        result.Value.AccountNumber.Should().Be("100000001");
        result.Value.OperationId.Should().NotBeEmpty();

        account.Balance.Amount.Should().Be(49000m);
        card.CurrentDebt.Amount.Should().Be(3000m);
        card.AvailableCredit.Amount.Should().Be(7000m);
        card.DomainEvents.OfType<CardPaymentProcessedEvent>().Should().ContainSingle();

        accountRepository.Verify(r => r.Update(account), Times.Once);
        cardRepository.Verify(r => r.Update(card), Times.Once);
        operationRepository.Verify(
            r => r.AddAsync(
                It.Is<FinancialOperation>(op =>
                    op.Kind == FinancialOperationKind.CreditCardPayment
                    && op.CreditCardId == card.Id
                    && op.RequestedAmount.Amount == 1000m
                    && op.AppliedAmount.Amount == 1000m
                    && op.AccountTransactions.Count == 1
                    && op.AccountTransactions.ElementAt(0).Direction == TransactionDirection.Debit
                    && op.AccountTransactions.ElementAt(0).BeneficiaryReference == "1234"
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
        emailService.Verify(
            s => s.SendAsync(
                "maria@artemis.com",
                It.IsAny<CardPaymentCompletedModel>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_Overpayment_DebitsOnlyRealDebt() {
        var card = SeedCard(debt: 4000m);
        var account = SeedAccount();
        var handler = CreateHandler(
            out _,
            out _,
            out var operationRepository,
            out _,
            card,
            account
        );

        var result = await handler.Handle(Command(10000m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(4000m);
        account.Balance.Amount.Should().Be(50000m - 4000m);
        card.CurrentDebt.Should().Be(Money.Zero);
        card.AvailableCredit.Amount.Should().Be(10000m);

        operationRepository.Verify(
            r => r.AddAsync(
                It.Is<FinancialOperation>(op =>
                    op.Kind == FinancialOperationKind.CreditCardPayment
                    && op.RequestedAmount.Amount == 10000m
                    && op.AppliedAmount.Amount == 4000m
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_UnknownCard_ReturnsNotFound() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            card: null,
            account: SeedAccount()
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Card.NotFound");
    }

    [Fact]
    public async Task Handle_CancelledCard_ReturnsNotActive() {
        var card = SeedCard(debt: 1000m);
        card.ApplyPayment(Money.Create(1000m).Value, FixedNow).IsSuccess.Should().BeTrue();
        card.Cancel(FixedNow.AddDays(1)).IsSuccess.Should().BeTrue();
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            card,
            SeedAccount()
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Card.NotActive");
    }

    [Fact]
    public async Task Handle_NoDebt_ReturnsNoDebt() {
        var card = SeedCard(debt: 0m);
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            card,
            SeedAccount()
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Card.NoDebt");
    }

    [Fact]
    public async Task Handle_UnknownAccount_ReturnsSourceNotFound() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            SeedCard(),
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
            SeedCard(),
            account
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotActive");
    }

    [Fact]
    public async Task Handle_InsufficientFunds_ReturnsDeclinedWithoutStateChanges() {
        var account = SeedAccount(initialBalance: 1000m);
        var card = SeedCard(debt: 4000m);
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            card,
            account
        );

        var result = await handler.Handle(Command(75000m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.InsufficientFunds");
        account.Balance.Amount.Should().Be(1000m);
        card.CurrentDebt.Amount.Should().Be(4000m);
    }

    [Fact]
    public async Task Handle_EmailFailure_KeepsPaymentSuccessful() {
        var card = SeedCard();
        var account = SeedAccount();
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out var emailService,
            card,
            account
        );
        emailService
            .Setup(s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<CardPaymentCompletedModel>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(new EmailSendException("Pago realizado a la tarjeta 1234", new IOException("smtp")));

        var result = await handler.Handle(Command(1000m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NotificationWarning.Should().Be(NotificationMessages.EmailFailed);
        account.Balance.Amount.Should().Be(49000m);
        card.CurrentDebt.Amount.Should().Be(3000m);
    }

    [Fact]
    public async Task Handle_DifferentAccountOwner_AlsoNotifiesAccountOwner() {
        var card = SeedCard();
        var account = SeedAccount(ownerUserId: "client-2");
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out var emailService,
            card,
            account,
            accountOwnerUserId: "client-2"
        );

        var result = await handler.Handle(Command(1000m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        emailService.Verify(
            s => s.SendAsync(
                "maria@artemis.com",
                It.IsAny<CardPaymentCompletedModel>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
        emailService.Verify(
            s => s.SendAsync(
                "pedro@artemis.com",
                It.IsAny<AccountDebitedForCardPaymentModel>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
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
        command.RequestFingerprint.Should().Be("1|100000001|1000.00");
    }
}
