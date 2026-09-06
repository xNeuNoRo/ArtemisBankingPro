using ArtemisBankingPro.Application.Common;
using System.Reflection;
using ArtemisBankingPro.Application.Features.HermesPay.Commands;
using ArtemisBankingPro.Application.Features.HermesPay.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.Security;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Domain.Operations.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.HermesPay.Handlers;

public sealed class ProcessHermesPayCommandHandlerTests {
    private const string CommerceUserId = "commerce-user-1";
    private const string CardOwnerId = "customer-1";
    private const string MerchantEmail = "comercio@example.com";
    private const string Pan = "1589963258467598";
    private const string CardLastFour = "7598";
    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string AccountNumberValue = "400000001";
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly FixedToday = new(2026, 8, 11);

    private sealed class FixedCvcVerifier(bool result) : ICvcVerifier {
        public bool Verify(string cvc, string storedDigest) => result;
    }

    private sealed class MutableCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId { get; set; } = CommerceUserId;
        public string? UserName => "actor";
        public string? Role { get; set; } = "Comercio";
        public int? CommerceId { get; set; } = 5;
    }

    private static Merchant NewMerchant(
        bool active = true,
        string? associatedUserId = CommerceUserId,
        int id = 5
    ) {
        var merchant = Merchant.Create(
            "Comercio Uno",
            null,
            MerchantEmail,
            "8095550101",
            "101000099",
            "admin",
            FixedNow.AddHours(-2)
        ).Value;
        typeof(Merchant)
            .GetProperty(nameof(Merchant.Id), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(merchant, id);

        if (!active) {
            merchant.Deactivate(FixedNow.AddHours(-1)).IsSuccess.Should().BeTrue();
        }

        if (associatedUserId is not null) {
            merchant.AssociateUser(associatedUserId, FixedNow.AddHours(-1))
                .IsSuccess
                .Should()
                .BeTrue();
        }

        return merchant;
    }

    private static SavingsAccount NewAccount(decimal initialBalance = 1000m) =>
        SavingsAccount.OpenPrimary(
            CommerceUserId,
            AccountNumber.Create(AccountNumberValue).Value,
            Money.Create(initialBalance).Value,
            "admin",
            FixedNow
        ).Value;

    private static CreditCardEntity NewCard(
        decimal limit = 10_000m,
        bool active = true,
        bool expired = false,
        int id = 77
    ) {
        DateTimeOffset issuedAt = FixedNow;
        DateOnly issueDate = FixedToday;
        if (expired) {
            issuedAt = new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);
            issueDate = new DateOnly(2020, 1, 1);
        }

        var card = CreditCardEntity.Issue(
            CardOwnerId,
            CardLastFour,
            Fingerprint,
            CvcDigest.Create(new string('c', 64)).Value,
            Money.Create(limit).Value,
            "admin",
            issuedAt,
            issueDate
        ).Value;
        typeof(CreditCardEntity)
            .GetProperty(nameof(CreditCardEntity.Id), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(card, id);

        if (!active) {
            card.Cancel(issuedAt.AddDays(1)).IsSuccess.Should().BeTrue();
        }

        return card;
    }

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

    private static Mock<IUserRepository> UserRepository(bool commerceUserActive = true) {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(r => r.GetByIdAsync(CardOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new UserListDto(
                    CardOwnerId,
                    "cliente01",
                    "001",
                    "María",
                    "Gómez",
                    "cliente@artemis.com",
                    "Cliente",
                    true,
                    FixedNow
                )
            );
        repository
            .Setup(r => r.GetByIdAsync(CommerceUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new UserListDto(
                    CommerceUserId,
                    "comercio01",
                    "101000099",
                    "Comercio",
                    "Uno",
                    MerchantEmail,
                    "Comercio",
                    commerceUserActive,
                    FixedNow
                )
            );
        return repository;
    }

    private static ProcessHermesPayCommandHandler CreateHandler(
        out Mock<ICreditCardRepository> cardRepository,
        out Mock<ISavingsAccountRepository> accountRepository,
        out Mock<IMerchantRepository> merchantRepository,
        out Mock<IFinancialOperationRepository> operationRepository,
        out Mock<IEmailService> emailService,
        out Func<FinancialOperation?> addedOperation,
        CreditCardEntity? card = null,
        SavingsAccount? account = null,
        Merchant? merchant = null,
        bool validCvc = true,
        string? role = "Comercio",
        int? commerceId = 5,
        string? currentUserId = CommerceUserId,
        bool commerceUserActive = true
    ) {
        var cardSecurityService = new Mock<ICardSecurityService>();
        cardSecurityService
            .Setup(s => s.ComputePanFingerprint(It.IsAny<string>()))
            .Returns(Fingerprint);

        cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(r => r.GetByPanFingerprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        accountRepository = new Mock<ISavingsAccountRepository>();
        accountRepository
            .Setup(r => r.GetPrincipalByCommerceIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        merchantRepository = new Mock<IMerchantRepository>();
        merchantRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(merchant);

        operationRepository = new Mock<IFinancialOperationRepository>();
        FinancialOperation? added = null;
        operationRepository
            .Setup(r => r.AddAsync(It.IsAny<FinancialOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (FinancialOperation operation, CancellationToken _) => {
                    added = operation;
                    return operation;
                }
            );
        addedOperation = () => added;

        emailService = new Mock<IEmailService>();

        var currentUser = new MutableCurrentUser {
            Role = role,
            CommerceId = commerceId,
            UserId = currentUserId
        };

        return new ProcessHermesPayCommandHandler(
            cardSecurityService.Object,
            new FixedCvcVerifier(validCvc),
            merchantRepository.Object,
            accountRepository.Object,
            cardRepository.Object,
            operationRepository.Object,
            UserRepository(commerceUserActive).Object,
            UnitOfWork().Object,
            Clock().Object,
            currentUser,
            emailService.Object,
            NullLogger<ProcessHermesPayCommandHandler>.Instance
        );
    }

    private static ProcessHermesPayCommand Command(
        int? commerceId = 5,
        decimal amount = 689.25m
    ) => new(commerceId, Pan, "08", "2029", "859", amount, "test-key");

    [Fact]
    public async Task Handle_ComercioRole_ApprovesPayment_CreditsCommerceAndIncreasesDebt() {
        var card = NewCard();
        var account = NewAccount();
        var merchant = NewMerchant();
        var handler = CreateHandler(
            out var cardRepository,
            out var accountRepository,
            out var merchantRepository,
            out var operationRepository,
            out var emailService,
            out var addedOperation,
            card,
            account,
            merchant
        );

        var result = await handler.Handle(
            Command(commerceId: 999),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Approved");
        result.Value.OperationId.Should().NotBeEmpty();

        merchantRepository.Verify(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        merchantRepository.Verify(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()), Times.Never);

        card.CurrentDebt.Amount.Should().Be(689.25m);
        account.Balance.Amount.Should().Be(1689.25m);

        cardRepository.Verify(r => r.Update(card), Times.Once);
        accountRepository.Verify(r => r.Update(account), Times.Once);

        FinancialOperation operation = addedOperation()!;
        operation.Kind.Should().Be(FinancialOperationKind.HermesPayment);
        operation.Status.Should().Be(FinancialOperationStatus.Approved);
        operation.RequestedAmount.Amount.Should().Be(689.25m);
        operation.AppliedAmount.Amount.Should().Be(689.25m);
        operation.MerchantId.Should().Be(merchant.Id);
        operation.CreditCardId.Should().Be(card.Id);

        operation.AccountTransactions.Should().ContainSingle(transaction =>
            transaction.Direction == TransactionDirection.Credit
            && transaction.AccountNumber.Value == AccountNumberValue
            && transaction.OriginReference == CardLastFour
            && transaction.BeneficiaryReference == AccountNumberValue
            && transaction.Amount.Amount == 689.25m
        );

        Assert.NotNull(operation.CardConsumption);
        operation.CardConsumption.MerchantId.Should().Be(merchant.Id);
        operation.CardConsumption.MerchantDisplayName.Should().Be("Comercio Uno");
        operation.CardConsumption.Type.Should().Be(ConsumptionType.Purchase);
        operation.CardConsumption.Amount.Amount.Should().Be(689.25m);

        HermesPayProcessedEvent domainEvent = Assert.IsType<HermesPayProcessedEvent>(
            operation.DomainEvents.OfType<HermesPayProcessedEvent>().Single()
        );
        domainEvent.CardLastFour.Should().Be(CardLastFour);
        domainEvent.MerchantId.Should().Be(merchant.Id);
        domainEvent.Amount.Amount.Should().Be(689.25m);
        domainEvent.CardOwnerUserId.Should().Be(CardOwnerId);
        domainEvent.MerchantEmail.Should().Be(MerchantEmail);
        domainEvent.OccurredAt.Should().Be(FixedNow);

        emailService.Verify(
            s => s.SendAsync(
                "cliente@artemis.com",
                It.IsAny<CardConsumptionMadeModel>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
        emailService.Verify(
            s => s.SendAsync(
                MerchantEmail,
                It.IsAny<PaymentReceivedByCommerceModel>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );

        operationRepository.Verify(
            r => r.AddAsync(It.IsAny<FinancialOperation>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_AdministradorRole_UsesCommandCommerceId() {
        var card = NewCard();
        var account = NewAccount();
        var merchant = NewMerchant();
        var handler = CreateHandler(
            out _,
            out _,
            out var merchantRepository,
            out _,
            out _,
            out _,
            card,
            account,
            merchant,
            role: "Administrador",
            commerceId: null
        );

        var result = await handler.Handle(Command(commerceId: 7), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        merchantRepository.Verify(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WrongRole_ReturnsForbidden() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            role: "Cajero"
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        var error = result.Error!;
        error.Code.Should().Be("Auth.Forbidden");
        error.Category.Should().Be(ErrorCategory.Forbidden);
    }

    [Fact]
    public async Task Handle_ComercioWithoutCommerce_ReturnsForbidden() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            commerceId: null
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        var error = result.Error!;
        error.Code.Should().Be("Commerce.NotAssociated");
        error.Category.Should().Be(ErrorCategory.Forbidden);
    }

    [Fact]
    public async Task Handle_AdminWithoutCommerceId_ReturnsValidation() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            role: "Administrador",
            commerceId: null
        );

        var result = await handler.Handle(Command(commerceId: null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.IdRequired");
    }

    [Fact]
    public async Task Handle_UnknownMerchant_ReturnsNotFound() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            merchant: null
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        var error = result.Error!;
        error.Code.Should().Be("Commerce.NotFound");
        error.Category.Should().Be(ErrorCategory.NotFound);
    }

    [Fact]
    public async Task Handle_InactiveMerchant_ReturnsValidation() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            merchant: NewMerchant(active: false)
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.Inactive");
    }

    [Fact]
    public async Task Handle_MerchantWithoutUser_ReturnsValidation() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            merchant: NewMerchant(associatedUserId: null)
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.NoAssociatedUser");
    }

    [Fact]
    public async Task Handle_MerchantWithoutPrincipalAccount_ReturnsValidation() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            merchant: NewMerchant(),
            account: null
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.NoPrincipalAccount");
    }

    [Fact]
    public async Task Handle_UnknownCard_ReturnsGenericValidation() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out var addedOperation,
            card: null,
            account: NewAccount(),
            merchant: NewMerchant()
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        var error = result.Error!;
        error.Code.Should().Be("Card.NotFound");
        error.Message.Should().Be("Los datos de la tarjeta no son válidos.");
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task Handle_InvalidCvc_ReturnsGenericValidation() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out var addedOperation,
            card: NewCard(),
            account: NewAccount(),
            merchant: NewMerchant(),
            validCvc: false
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        var error = result.Error!;
        error.Code.Should().Be("Card.InvalidCvc");
        error.Message.Should().Be("Los datos de la tarjeta no son válidos.");
        FinancialOperation operation = addedOperation()!;
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Card.InvalidCvc");
    }

    [Fact]
    public async Task Handle_ExpiredCard_ReturnsGenericValidation() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out var addedOperation,
            card: NewCard(expired: true),
            account: NewAccount(),
            merchant: NewMerchant()
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        var error = result.Error!;
        error.Code.Should().Be("Card.Expired");
        error.Message.Should().Be("Los datos de la tarjeta no son válidos.");
        FinancialOperation operation = addedOperation()!;
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Card.Expired");
    }

    [Fact]
    public async Task Handle_CancelledCard_ReturnsGenericValidation() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out var addedOperation,
            card: NewCard(active: false),
            account: NewAccount(),
            merchant: NewMerchant()
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        var error = result.Error!;
        error.Code.Should().Be("Card.NotActive");
        error.Message.Should().Be("Los datos de la tarjeta no son válidos.");
        FinancialOperation operation = addedOperation()!;
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Card.NotActive");
    }

    [Fact]
    public async Task Handle_InsufficientCredit_RecordsRejectedConsumptionWithoutStateChanges() {
        var card = NewCard(limit: 500m);
        var account = NewAccount();
        var merchant = NewMerchant();
        var handler = CreateHandler(
            out var cardRepository,
            out var accountRepository,
            out _,
            out _,
            out var emailService,
            out var addedOperation,
            card,
            account,
            merchant
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Card.InsufficientCredit");
        result.Error.Message.Should().Be(
            "El monto de la transacción excede el crédito disponible de la tarjeta."
        );

        card.CurrentDebt.Amount.Should().Be(0m);
        account.Balance.Amount.Should().Be(1000m);

        cardRepository.Verify(r => r.Update(card), Times.Never);
        accountRepository.Verify(r => r.Update(account), Times.Never);
        emailService.Verify(
            s => s.SendAsync(It.IsAny<string>(), It.IsAny<CardConsumptionMadeModel>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        emailService.Verify(
            s => s.SendAsync(It.IsAny<string>(), It.IsAny<PaymentReceivedByCommerceModel>(), It.IsAny<CancellationToken>()),
            Times.Never
        );

        FinancialOperation operation = addedOperation()!;
        operation.Kind.Should().Be(FinancialOperationKind.HermesPayment);
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Card.InsufficientCredit");
        operation.RequestedAmount.Amount.Should().Be(689.25m);
        operation.AppliedAmount.Amount.Should().Be(0m);
        operation.AccountTransactions.Should().BeEmpty();
        Assert.NotNull(operation.CardConsumption);
        operation.CardConsumption.Type.Should().Be(ConsumptionType.Purchase);
        operation.CardConsumption.MerchantId.Should().Be(merchant.Id);
    }

    [Fact]
    public async Task Handle_EmailFailure_KeepsPaymentSuccessful() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out var emailService,
            out _,
            NewCard(),
            NewAccount(),
            NewMerchant()
        );
        emailService
            .Setup(s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<CardConsumptionMadeModel>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(
                new EmailSendException(
                    "Consumo realizado con la tarjeta 7598",
                    new IOException("smtp")
                )
            );
        emailService
            .Setup(s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<PaymentReceivedByCommerceModel>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(
                new EmailSendException(
                    "Pago recibido por el comercio",
                    new IOException("smtp")
                )
            );

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NotificationWarning.Should().Be(NotificationMessages.EmailFailed);
    }

    [Fact]
    public void Command_FingerprintExcludesSensitiveCardMaterial() {
        var command = Command();

        string key = command.IdempotencyKey;
        string fingerprint = command.RequestFingerprint;
        key.Should().Be("test-key");
        key.Should().NotContain(Pan);
        key.Should().NotContain("859");
        fingerprint.Should().NotContain(Pan);
        fingerprint.Should().NotContain("859");
    }

    [Fact]
    public void Command_FingerprintIncludesEffectiveCommerce() {
        ProcessHermesPayCommand first = Command() with { CommerceId = 1 };
        ProcessHermesPayCommand second = Command() with { CommerceId = 2 };

        first.RequestFingerprint.Should().NotBe(second.RequestFingerprint);
    }

    [Fact]
    public async Task Handle_WrongExpirationMonth_ReturnsGenericValidation() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out var addedOperation,
            card: NewCard(),
            account: NewAccount(),
            merchant: NewMerchant()
        );

        var result = await handler.Handle(
            Command() with { MonthExpirationCard = "05" },
            CancellationToken.None
        );

        var error = result.Error!;
        error.Code.Should().Be("Card.Expired");
        error.Message.Should().Be("Los datos de la tarjeta no son válidos.");
        FinancialOperation operation = addedOperation()!;
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Card.Expired");
    }

    [Fact]
    public async Task Handle_WrongExpirationYear_ReturnsGenericValidation() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out var addedOperation,
            card: NewCard(),
            account: NewAccount(),
            merchant: NewMerchant()
        );

        var result = await handler.Handle(
            Command() with { YearExpirationCard = "2030" },
            CancellationToken.None
        );

        var error = result.Error!;
        error.Code.Should().Be("Card.Expired");
        error.Message.Should().Be("Los datos de la tarjeta no son válidos.");
        FinancialOperation operation = addedOperation()!;
        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RejectionCode.Should().Be("Card.Expired");
    }

    [Fact]
    public async Task Handle_ComercioRole_ReassignedCommerceUser_ReturnsForbidden() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out var addedOperation,
            card: NewCard(),
            account: NewAccount(),
            merchant: NewMerchant(),
            currentUserId: "another-user"
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        var error = result.Error!;
        error.Code.Should().Be("Commerce.NotAssociated");
        Assert.Null(addedOperation());
    }

    [Fact]
    public async Task Handle_ComercioRole_InactiveUser_ReturnsForbidden() {
        var handler = CreateHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out var addedOperation,
            card: NewCard(),
            account: NewAccount(),
            merchant: NewMerchant(),
            commerceUserActive: false
        );

        var result = await handler.Handle(Command(), CancellationToken.None);

        var error = result.Error!;
        error.Code.Should().Be("Auth.InactiveUser");
        Assert.Null(addedOperation());
    }
}
