using ArtemisBankingPro.Application.Common;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.HermesPay.Commands;
using ArtemisBankingPro.Application.Features.HermesPay.DTOs;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Cards.Security;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;
using Microsoft.Extensions.Logging;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.Application.Features.HermesPay.Handlers;

/// <summary>
/// Procesa un pago con tarjeta de crédito a favor de un comercio (spec §41,
/// POST /pay/process-payment/{commerceId}): valida el rol y el comercio según
/// el actor autenticado, verifica la tarjeta (huella HMAC del PAN, CVC en
/// tiempo fijo, expiración y estado) y ejecuta de forma atómica el aumento de
/// la deuda, el consumo, el crédito a la cuenta principal del comercio y la
/// operación financiera <c>HermesPayment</c>. Un intento sin crédito
/// disponible registra un consumo RECHAZADO sin tocar balances ni acreditar al
/// comercio. Los correos se envían después del commit y su fallo no revierte
/// el pago. La validación de rol es manual (no implementa <see cref="IAuthorize"/>).
/// </summary>
public sealed class ProcessHermesPayCommandHandler
    : IRequestHandler<ProcessHermesPayCommand, Result<ProcessHermesPayResponse>> {
    /// <summary>
    /// Mensaje genérico para fallos de datos de tarjeta: no revela si el
    /// número pertenece a una tarjeta registrada (spec §41, respuesta 400).
    /// </summary>
    private const string CardDataInvalidMessage = "Los datos de la tarjeta no son válidos.";

    private readonly ICardSecurityService _cardSecurityService;
    private readonly ICvcVerifier _cvcVerifier;
    private readonly IMerchantRepository _merchantRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessHermesPayCommandHandler> _logger;

    public ProcessHermesPayCommandHandler(
        ICardSecurityService cardSecurityService,
        ICvcVerifier cvcVerifier,
        IMerchantRepository merchantRepository,
        ISavingsAccountRepository savingsAccountRepository,
        ICreditCardRepository creditCardRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessHermesPayCommandHandler> logger
    ) {
        _cardSecurityService = cardSecurityService;
        _cvcVerifier = cvcVerifier;
        _merchantRepository = merchantRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _creditCardRepository = creditCardRepository;
        _financialOperationRepository = financialOperationRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _currentUser = currentUser;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<ProcessHermesPayResponse>> Handle(
        ProcessHermesPayCommand message,
        CancellationToken cancellationToken
    ) {
        // Autorización por rol: solo Administrador o Comercio.
        string? role = _currentUser.Role;
        if (role is not nameof(Roles.Administrador) and not nameof(Roles.Comercio)) {
            return Result.Failure<ProcessHermesPayResponse>(
                DomainError.Forbidden(
                    "Auth.Forbidden",
                    "El rol del usuario no tiene permisos para esta operación."
                )
            );
        }

        bool isCommerceRole = role == nameof(Roles.Comercio);

        // Resolvemos el comercio: JWT para Comercio (ignora el valor recibido)
        // o commerceId del command para Administrador.
        int? resolvedCommerceId = isCommerceRole ? _currentUser.CommerceId : message.CommerceId;
        if (resolvedCommerceId is null or <= 0) {
            return Result.Failure<ProcessHermesPayResponse>(
                isCommerceRole
                    ? DomainError.Forbidden(
                        "Commerce.NotAssociated",
                        "El usuario de comercio no tiene un comercio asociado."
                    )
                    : DomainError.Validation(
                        "Commerce.IdRequired",
                        "Debe indicar el comercio que recibirá el pago."
                    )
            );
        }

        // Comercio existente, activo y con usuario asociado.
        var merchant = await _merchantRepository.GetByIdAsync(
            resolvedCommerceId.Value,
            cancellationToken
        );
        if (merchant is null) {
            return Result.Failure<ProcessHermesPayResponse>(
                DomainError.NotFound("Commerce.NotFound", "El comercio indicado no existe.")
            );
        }

        if (merchant.Status != MerchantStatus.Active) {
            return Result.Failure<ProcessHermesPayResponse>(
                DomainError.Validation(
                    "Commerce.Inactive",
                    "El comercio está inactivo y no puede procesar pagos."
                )
            );
        }

        if (string.IsNullOrWhiteSpace(merchant.AssociatedUserId)) {
            return Result.Failure<ProcessHermesPayResponse>(
                DomainError.Validation(
                    "Commerce.NoAssociatedUser",
                    "El comercio no tiene un usuario asociado."
                )
            );
        }

        // Para rol Comercio, el vínculo vigente y el usuario activo se
        // revalidan contra el JWT actual (no solo contra CommerceId): un JWT
        // emitido antes de una reasignación o de una inactivación no sigue
        // operando sobre el comercio (spec §41, ADR-002 §3).
        if (isCommerceRole) {
            if (merchant.AssociatedUserId != _currentUser.UserId) {
                return Result.Failure<ProcessHermesPayResponse>(
                    DomainError.Forbidden(
                        "Commerce.NotAssociated",
                        "El usuario de comercio no tiene un comercio asociado."
                    )
                );
            }

            var commerceUser = await _userRepository.GetByIdAsync(
                _currentUser.UserId,
                cancellationToken
            );
            if (commerceUser is null || !commerceUser.IsActive) {
                return Result.Failure<ProcessHermesPayResponse>(
                    DomainError.Forbidden(
                        "Auth.InactiveUser",
                        "El usuario de comercio está inactivo."
                    )
                );
            }
        }

        // Cuenta principal activa del comercio.
        var account = await _savingsAccountRepository.GetPrincipalByCommerceIdAsync(
            resolvedCommerceId.Value,
            cancellationToken
        );
        if (account is null) {
            return Result.Failure<ProcessHermesPayResponse>(
                DomainError.Validation(
                    "Commerce.NoPrincipalAccount",
                    "El comercio no tiene una cuenta de ahorro principal activa."
                )
            );
        }

        // Tarjeta por huella HMAC del PAN. Los fallos de datos de tarjeta
        // usan un mensaje genérico para no revelar existencia.
        string panFingerprint = _cardSecurityService.ComputePanFingerprint(message.CardNumber);
        var card = await _creditCardRepository.GetByPanFingerprintAsync(
            panFingerprint,
            cancellationToken
        );
        if (card is null) {
            return Result.Failure<ProcessHermesPayResponse>(
                DomainError.Validation("Card.NotFound", CardDataInvalidMessage)
            );
        }

        if (message.TransactionAmount <= 0m) {
            return Result.Failure<ProcessHermesPayResponse>(CardErrors.AmountMustBePositive);
        }

        var amountResult = Money.Create(message.TransactionAmount);
        if (amountResult.IsFailure) {
            return Result.Failure<ProcessHermesPayResponse>(amountResult.Error!);
        }

        Money amount = amountResult.Value;
        DateTimeOffset occurredAt = _clock.Now;
        Guid operationId = Guid.NewGuid();

        // CVC: comparación en tiempo fijo contra el digest almacenado.
        var cvcResult = card.VerifyCvc(message.Cvc, _cvcVerifier);
        if (cvcResult.IsFailure) {
            return await RejectBusinessFailureAsync(
                operationId,
                merchant,
                card,
                amount,
                occurredAt,
                cvcResult.Error!,
                cancellationToken
            );
        }

        // Expiración y estado (se revalidan dentro de la transacción). La
        // fecha enviada debe coincidir con la de la tarjeta: un PAN/CVC válido
        // con mes/año incorrecto se rechaza con el mismo mensaje genérico.
        if (card.Expiration.IsExpired(_clock.Today)) {
            return await RejectBusinessFailureAsync(
                operationId,
                merchant,
                card,
                amount,
                occurredAt,
                DomainError.Validation("Card.Expired", CardDataInvalidMessage),
                cancellationToken
            );
        }

        if (
            !int.TryParse(message.MonthExpirationCard, out int expirationMonth)
            || !int.TryParse(message.YearExpirationCard, out int expirationYear)
            || !card.Expiration.Matches(expirationMonth, expirationYear)
        ) {
            return await RejectBusinessFailureAsync(
                operationId,
                merchant,
                card,
                amount,
                occurredAt,
                DomainError.Validation("Card.Expired", CardDataInvalidMessage),
                cancellationToken
            );
        }

        if (card.Status != CreditCardStatus.Active) {
            return await RejectBusinessFailureAsync(
                operationId,
                merchant,
                card,
                amount,
                occurredAt,
                DomainError.Validation("Card.NotActive", CardDataInvalidMessage),
                cancellationToken
            );
        }

        // Deuda + crédito al comercio + consumo + operación, atómicos. La
        // revalidación del crédito disponible ocurre dentro de la transacción
        // y el rowversion de la tarjeta impide que dos consumos concurrentes
        // excedan el límite: el perdedor obtiene conflicto de concurrencia.
        // Rechazo por crédito insuficiente: se persiste en su propia
        // transacción confirmada (ADR-002 §2) y se devuelve el error fuera de
        // ella. Un Result.Failure nunca confirma la transacción, por lo que el
        // consumo RECHAZADO no puede vivir dentro del bloque atómico.
        var canChargeResult = card.CanAuthorizeCharge(amount, _clock.Today);
        if (canChargeResult.IsFailure) {
            DomainError canChargeError = canChargeResult.Error!;
            if (canChargeError.Code == CardErrors.InsufficientCredit.Code) {
                Result rejection = await RecordRejectedConsumptionAsync(
                    operationId,
                    merchant,
                    card,
                    amount,
                    occurredAt,
                    canChargeError.Code,
                    cancellationToken
                );
                if (rejection.IsFailure) {
                    return Result.Failure<ProcessHermesPayResponse>(rejection.Error!);
                }

                return Result.Failure<ProcessHermesPayResponse>(
                    DomainError.Declined(
                        "Card.InsufficientCredit",
                        "El monto de la transacción excede el crédito disponible de la tarjeta."
                    )
                );
            }

            return await RejectBusinessFailureAsync(
                operationId,
                merchant,
                card,
                amount,
                occurredAt,
                canChargeError,
                cancellationToken
            );
        }

        var persistResult = await ExecutePaymentAsync(
            operationId,
            merchant,
            account,
            card,
            amount,
            message.Cvc,
            expirationMonth,
            expirationYear,
            isCommerceRole,
            occurredAt,
            cancellationToken
        );
        if (persistResult.IsFailure) {
            DomainError persistError = persistResult.Error!;
            if (persistError.Code == CardErrors.InsufficientCredit.Code) {
                Result rejection = await RecordRejectedConsumptionAsync(
                    operationId,
                    merchant,
                    card,
                    amount,
                    occurredAt,
                    persistError.Code,
                    cancellationToken
                );
                if (rejection.IsFailure) {
                    return Result.Failure<ProcessHermesPayResponse>(rejection.Error!);
                }

                return Result.Failure<ProcessHermesPayResponse>(
                    DomainError.Declined(
                        "Card.InsufficientCredit",
                        "El monto de la transacción excede el crédito disponible de la tarjeta."
                    )
                );
            }

            return await RejectBusinessFailureAsync(
                operationId,
                merchant,
                card,
                amount,
                occurredAt,
                persistError,
                cancellationToken
            );
        }

        // Correos post-commit (fallo no revierte el pago). La notificación no
        // debe convertir una operación ya confirmada en una cancelación HTTP.
        bool notificationsOk = await SendNotificationsAsync(
            card,
            merchant,
            amount,
            occurredAt,
            CancellationToken.None
        );

        return Result.Success(
            new ProcessHermesPayResponse(
                operationId,
                "Approved",
                NotificationWarning: notificationsOk ? null : NotificationMessages.EmailFailed
            )
        );
    }

    private async Task<Result> ExecutePaymentAsync(
        Guid operationId,
        Merchant merchant,
        SavingsAccount account,
        CreditCardEntity card,
        Money amount,
        string cvc,
        int expirationMonth,
        int expirationYear,
        bool isCommerceRole,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    ) {
        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                // Revalidaciones dentro de la transacción para defenderse de
                // carreras de lectura-escritura solapadas.
                await _merchantRepository.ReloadAsync(merchant, ct);
                if (merchant.Status != MerchantStatus.Active) {
                    return Result.Failure(
                        DomainError.Validation(
                            "Commerce.Inactive",
                            "El comercio está inactivo y no puede procesar pagos."
                        )
                    );
                }

                if (string.IsNullOrWhiteSpace(merchant.AssociatedUserId)) {
                    return Result.Failure(
                        DomainError.Validation(
                            "Commerce.NoAssociatedUser",
                            "El comercio no tiene un usuario asociado."
                        )
                    );
                }

                if (isCommerceRole && merchant.AssociatedUserId != _currentUser.UserId) {
                    return Result.Failure(
                        DomainError.Forbidden(
                            "Commerce.NotAssociated",
                            "El usuario de comercio no tiene un comercio asociado."
                        )
                    );
                }

                await _savingsAccountRepository.ReloadAsync(account, ct);
                if (account.Status != AccountStatus.Active) {
                    return Result.Failure(AccountErrors.NotActive);
                }

                await _creditCardRepository.ReloadAsync(card, ct);
                if (card.Status != CreditCardStatus.Active) {
                    return Result.Failure(
                        DomainError.Validation("Card.NotActive", CardDataInvalidMessage)
                    );
                }

                Result cvcResult = card.VerifyCvc(cvc, _cvcVerifier);
                if (cvcResult.IsFailure) {
                    return cvcResult;
                }

                if (
                    card.Expiration.IsExpired(_clock.Today)
                    || !card.Expiration.Matches(expirationMonth, expirationYear)
                ) {
                    return Result.Failure(
                        DomainError.Validation("Card.Expired", CardDataInvalidMessage)
                    );
                }

                // Revalidar el crédito disponible sin mutar. Una carrera de
                // escritura entre dos pagos la resuelve el rowversion de la
                // tarjeta (el perdedor recibe conflicto de concurrencia); la
                // revalidación es la defensa en profundidad para el caso de
                // lectura-escritura solapada.
                var canChargeResult = card.CanAuthorizeCharge(amount, _clock.Today);
                if (canChargeResult.IsFailure) {
                    return canChargeResult;
                }

                var canCreditResult = account.CanCredit(amount);
                if (canCreditResult.IsFailure) {
                    return canCreditResult;
                }

                // Mutaciones atómicas. Orden estable: tarjeta antes que
                // cuenta para reducir deadlocks.
                var chargeResult = card.AuthorizeCharge(amount, _clock.Today);
                if (chargeResult.IsFailure) {
                    return chargeResult;
                }

                _creditCardRepository.Update(card);

                var creditResult = account.Credit(amount);
                if (creditResult.IsFailure) {
                    return creditResult;
                }

                _savingsAccountRepository.Update(account);

                // Operación aprobada: consumo APROBADO y CRÉDITO en la
                // cuenta principal del comercio (origen: últimos cuatro de la
                // tarjeta; beneficiario: cuenta del comercio).
                var operationResult = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.HermesPayment,
                    amount,
                    amount,
                    Money.Zero,
                    _currentUser.UserId!,
                    occurredAt,
                    [
                        new AccountTransactionDetails(
                            account.Number,
                            TransactionDirection.Credit,
                            amount,
                            card.LastFour,
                            account.Number.Value
                        ),
                    ],
                    new CardConsumptionDetails(
                        card.Id,
                        merchant.Id,
                        merchant.Name,
                        ConsumptionType.Purchase,
                        amount
                    ),
                    creditCardId: card.Id,
                    merchantId: merchant.Id
                );
                if (operationResult.IsFailure) {
                    return Result.Failure(operationResult.Error!);
                }

                var operation = operationResult.Value;
                Result eventResult = operation.RecordHermesPayProcessed(
                    card.LastFour,
                    merchant.Id,
                    merchant.Name,
                    amount,
                    card.CustomerUserId,
                    merchant.Email
                );
                if (eventResult.IsFailure) {
                    return eventResult;
                }

                await _financialOperationRepository.AddAsync(operation, ct);
                return Result.Success();
            },
            ct: cancellationToken
        );
    }

    private async Task<Result> RecordRejectedConsumptionAsync(
        Guid operationId,
        Merchant merchant,
        CreditCardEntity card,
        Money amount,
        DateTimeOffset occurredAt,
        string rejectionCode,
        CancellationToken ct
    ) {
        // Rechazo por falta de crédito disponible (spec §41): consumo RECHAZADO
        // sin modificar balances, deuda ni acreditar al comercio. Se persiste en
        // su propia transacción confirmada (ADR-002 §2: el rechazo nunca vive en
        // el bloque atómico que mutaría estado, y un Result.Failure revierte).
        var rejectedOperation = FinancialOperation.Reject(
            operationId,
            FinancialOperationKind.HermesPayment,
            amount,
            Money.Zero,
            _currentUser.UserId!,
            occurredAt,
            rejectionCode,
            [],
            new CardConsumptionDetails(
                card.Id,
                merchant.Id,
                merchant.Name,
                ConsumptionType.Purchase,
                amount
            ),
            creditCardId: card.Id,
            merchantId: merchant.Id
        );
        if (rejectedOperation.IsFailure) {
            return Result.Failure(rejectedOperation.Error!);
        }

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token => {
                await _financialOperationRepository.AddAsync(rejectedOperation.Value, token);
                return Result.Success();
            },
            ct: ct
        );
    }

    private async Task<Result<ProcessHermesPayResponse>> RejectBusinessFailureAsync(
        Guid operationId,
        Merchant merchant,
        CreditCardEntity card,
        Money amount,
        DateTimeOffset occurredAt,
        DomainError error,
        CancellationToken cancellationToken
    ) {
        if (!IsBusinessRejection(error)) {
            return Result.Failure<ProcessHermesPayResponse>(error);
        }

        Result persisted = await RecordRejectedConsumptionAsync(
            operationId,
            merchant,
            card,
            amount,
            occurredAt,
            error.Code,
            cancellationToken
        );
        return persisted.IsFailure
            ? Result.Failure<ProcessHermesPayResponse>(persisted.Error!)
            : Result.Failure<ProcessHermesPayResponse>(error);
    }

    private static bool IsBusinessRejection(DomainError error) =>
        !error.Code.StartsWith("Concurrency.", StringComparison.Ordinal)
        && !error.Code.StartsWith("Persistence.", StringComparison.Ordinal)
        && !string.Equals(error.Code, "Internal.Unexpected", StringComparison.Ordinal);

    private async Task<bool> SendNotificationsAsync(
        CreditCardEntity card,
        Merchant merchant,
        Money amount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    ) {
        // Correo al cliente propietario de la tarjeta (spec §41).
        bool allSent = true;
        var cardOwner = await _userRepository.GetByIdAsync(card.CustomerUserId, cancellationToken);
        if (cardOwner is not null) {
            allSent =
                (
                    await TrySendAsync(
                        cardOwner.Email,
                        new CardConsumptionMadeModel(
                            $"{cardOwner.FirstName} {cardOwner.LastName}".Trim(),
                            card.LastFour,
                            merchant.Name,
                            amount,
                            occurredAt,
                            _clock.BusinessTimeZone
                        ),
                        card.LastFour,
                        cancellationToken
                    )
                ) && allSent;
        }

        // Correo al comercio receptor del pago (spec §41).
        return (
                await TrySendAsync(
                    merchant.Email,
                    new PaymentReceivedByCommerceModel(
                        merchant.Name,
                        card.LastFour,
                        amount,
                        occurredAt,
                        _clock.BusinessTimeZone
                    ),
                    card.LastFour,
                    cancellationToken
                )
            ) && allSent;
    }

    private async Task<bool> TrySendAsync<T>(
        string recipient,
        T model,
        string cardLastFour,
        CancellationToken cancellationToken
    )
        where T : IEmailModel {
        try {
            await _emailService.SendAsync(recipient, model, cancellationToken);
            return true;
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo {Template} tras el pago Hermes Pay de la tarjeta {CardLastFour}.",
                model.TemplateName,
                cardLastFour
            );
            return false;
        }
    }
}
