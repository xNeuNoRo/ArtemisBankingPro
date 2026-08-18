using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.CreditCard.Handlers;

public sealed class AssignCreditCardCommandHandlerTests {
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateOnly FixedToday = new(2026, 8, 10);
    private const string Pan = "9000001234567890";
    private const string LastFourValue = "7890";
    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string CvcDigestHex = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

    private static UserListDto ActiveClient(bool active = true) =>
        new(
            "client-1",
            "cliente01",
            "001",
            "María",
            "Gómez",
            "maria@artemis.com",
            "Cliente",
            active,
            FixedNow
        );

    private static Mock<IUserRepository> UserRepository(UserListDto? client = null) {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(r => r.GetByIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(client ?? ActiveClient());
        repository
            .Setup(r => r.GetRolesAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Cliente"]);
        return repository;
    }

    private static Mock<ISavingsAccountRepository> SavingsAccountRepository(SavingsAccount? account) {
        var repository = new Mock<ISavingsAccountRepository>();
        repository
            .Setup(r => r.GetPrincipalByOwnerAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        return repository;
    }

    private static SavingsAccount SeedPrincipalAccount() {
        var accountNumber = AccountNumber.Create("123456789").Value;
        return SavingsAccount.OpenPrimary(
            "client-1",
            accountNumber,
            Money.Zero,
            "admin-1",
            FixedNow
        ).Value;
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

    private static Mock<INumberGenerator> NumberGenerator() {
        var generator = new Mock<INumberGenerator>();
        generator
            .Setup(g => g.NextCardNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pan);
        return generator;
    }

    private static Mock<ICardSecurityService> CardSecurityService() {
        var service = new Mock<ICardSecurityService>();
        service
            .Setup(s => s.ComputePanFingerprint(It.IsAny<string>()))
            .Returns(Fingerprint);
        service
            .Setup(s => s.ComputeCvcDigest(It.IsAny<string>()))
            .Returns(CvcDigestHex);
        return service;
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

    private static AssignCreditCardCommandHandler CreateHandler(
        out Func<CreditCardEntity?> addedCard,
        out Func<FinancialOperation?> addedOperation,
        Mock<IUserRepository>? userRepository = null,
        Mock<ISavingsAccountRepository>? accountRepository = null,
        Mock<IEmailService>? emailService = null
    ) {
        CreditCardEntity? card = null;
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.AddAsync(It.IsAny<CreditCardEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (CreditCardEntity value, CancellationToken _) => {
                    card = value;
                    return value;
                }
            );
        addedCard = () => card;
        var operationRepository = OperationRepository(out addedOperation);
        return new AssignCreditCardCommandHandler(
            (userRepository ?? UserRepository()).Object,
            (accountRepository ?? SavingsAccountRepository(SeedPrincipalAccount())).Object,
            cardRepository.Object,
            operationRepository.Object,
            NumberGenerator().Object,
            CardSecurityService().Object,
            UnitOfWork().Object,
            Clock().Object,
            CurrentUser().Object,
            (emailService ?? new Mock<IEmailService>()).Object,
            NullLogger<AssignCreditCardCommandHandler>.Instance
        );
    }

    [Fact]
    public async Task Handle_ActiveClientWithPrincipalAccount_IssuesCardAndRecordsOperation() {
        var handler = CreateHandler(
            out Func<CreditCardEntity?> addedCard,
            out Func<FinancialOperation?> addedOperation
        );

        var result = await handler.Handle(
            new AssignCreditCardCommand("client-1", 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.MaskedNumber.Should().Be($"************{LastFourValue}");
        result.Value.LastFour.Should().Be(LastFourValue);
        result.Value.ClientId.Should().Be("client-1");
        result.Value.ClientFullName.Should().Be("María Gómez");
        result.Value.CreditLimit.Should().Be(15_000m);
        result.Value.AvailableCredit.Should().Be(15_000m);
        result.Value.Expiration.Should().Be("08/29");
        result.Value.Status.Should().Be("Active");
        result.Value.CreatedAt.Should().Be(FixedNow);

        Assert.NotNull(addedCard());
        addedCard()!.CustomerUserId.Should().Be("client-1");
        addedCard()!.LastFour.Should().Be(LastFourValue);
        addedCard()!.PanFingerprint.Should().Be(Fingerprint);
        addedCard()!.CreditLimit.Amount.Should().Be(15_000m);
        addedCard()!.AssignedByUserId.Should().Be("admin-1");

        Assert.NotNull(addedOperation());
        addedOperation()!.Kind.Should().Be(FinancialOperationKind.CardAssigned);
        addedOperation()!.RequestedAmount.Should().Be(Money.Zero);
        addedOperation()!.AppliedAmount.Should().Be(Money.Zero);
        addedOperation()!.CreditCardId.Should().BeNull();
        addedOperation()!.InitiatedByUserId.Should().Be("admin-1");
        addedOperation()!.AccountTransactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ActiveClient_SendsAssignedEmailAfterCommit() {
        var emailService = new Mock<IEmailService>();
        var handler = CreateHandler(
            out _,
            out Func<FinancialOperation?> addedOperation,
            emailService: emailService
        );

        var result = await handler.Handle(
            new AssignCreditCardCommand("client-1", 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        Assert.NotNull(addedOperation());
        emailService.Verify(
            service => service.SendAsync(
                "maria@artemis.com",
                It.Is<CardAssignedModel>(model =>
                    model.CustomerName == "María Gómez"
                    && model.LastFour == LastFourValue
                    && model.CreditLimit.Amount == 15_000m
                    && model.Expiration == "08/29"
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_UnknownCustomer_ReturnsCustomerNotFoundWithoutPersistence() {
        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserListDto?)null);
        var handler = CreateHandler(
            out _,
            out Func<FinancialOperation?> addedOperation,
            userRepository: userRepository
        );

        var result = await handler.Handle(
            new AssignCreditCardCommand("ghost", 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.CustomerNotFound");
        result.Error.Category.Should().Be(ErrorCategory.NotFound);
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task Handle_InactiveCustomer_ReturnsCustomerNotActiveWithoutPersistence() {
        var userRepository = UserRepository(ActiveClient(active: false));
        var handler = CreateHandler(
            out _,
            out Func<FinancialOperation?> addedOperation,
            userRepository: userRepository
        );

        var result = await handler.Handle(
            new AssignCreditCardCommand("client-1", 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.CustomerNotActive");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task Handle_CustomerWithoutClientRole_ReturnsCustomerNotClientWithoutPersistence() {
        var userRepository = UserRepository();
        userRepository
            .Setup(r => r.GetRolesAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Cajero"]);
        var handler = CreateHandler(
            out _,
            out Func<FinancialOperation?> addedOperation,
            userRepository: userRepository
        );

        var result = await handler.Handle(
            new AssignCreditCardCommand("client-1", 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.CustomerNotClient");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task Handle_CustomerWithoutPrincipalAccount_ReturnsNoPrincipalAccount() {
        var accountRepository = SavingsAccountRepository(account: null);
        var handler = CreateHandler(
            out _,
            out Func<FinancialOperation?> addedOperation,
            accountRepository: accountRepository
        );

        var result = await handler.Handle(
            new AssignCreditCardCommand("client-1", 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.NoPrincipalAccount");
        result.Error.Category.Should().Be(ErrorCategory.PreconditionFailed);
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task Handle_NonPositiveLimit_ReturnsLimitMustBePositiveWithoutPersistence() {
        var handler = CreateHandler(
            out _,
            out Func<FinancialOperation?> addedOperation
        );

        var result = await handler.Handle(
            new AssignCreditCardCommand("client-1", 0m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Card.LimitMustBePositive");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task Handle_EmailFailure_KeepsAssignmentSuccessful() {
        var emailService = new Mock<IEmailService>();
        emailService
            .Setup(service => service.SendAsync(
                It.IsAny<string>(),
                It.IsAny<CardAssignedModel>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(
                new EmailSendException(
                    "Nueva tarjeta de crédito asignada",
                    new InvalidOperationException("Fallo simulado del proveedor.")
                )
            );
        var handler = CreateHandler(
            out Func<CreditCardEntity?> addedCard,
            out Func<FinancialOperation?> addedOperation,
            emailService: emailService
        );

        var result = await handler.Handle(
            new AssignCreditCardCommand("client-1", 15_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        Assert.NotNull(addedCard());
        Assert.NotNull(addedOperation());
    }

    [Fact]
    public void Command_ImplementsIdempotencyWithKeyIncludingCustomerAndLimit() {
        var command = new AssignCreditCardCommand("client-1", 15_000m) {
            IdempotencyKey = "card-key-1",
        };

        command.IdempotencyKey.Should().Be("card-key-1");
        command.RequestFingerprint.Should().Be("client-1|15000");
    }

    [Fact]
    public void Command_RequiresAdministratorRole() {
        var command = new AssignCreditCardCommand("client-1", 15_000m);

        command.RequiredRoles.Should().BeEquivalentTo("Administrador");
    }
}
