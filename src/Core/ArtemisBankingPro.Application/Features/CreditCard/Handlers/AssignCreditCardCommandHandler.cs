using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Common;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using Mediator;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Security.Cryptography;

namespace ArtemisBankingPro.Application.Features.CreditCard.Handlers;

/// <summary>
/// Asigna una nueva tarjeta de crédito a un cliente activo con una cuenta
/// principal activa. Genera el número de 16 dígitos con el BIN propio (Luhn),
/// descarta el CVC tras persistir su digest HMAC-SHA256 y registra la operación
/// <see cref="FinancialOperationKind.CardAssigned"/> en la misma transacción.
/// El correo se envía después del commit y su fallo no revierte la asignación.
/// </summary>
public sealed class AssignCreditCardCommandHandler
    : IRequestHandler<AssignCreditCardCommand, Result<AssignCreditCardResponse>> {
    private readonly IUserRepository _userRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly INumberGenerator _numberGenerator;
    private readonly ICardSecurityService _cardSecurityService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<AssignCreditCardCommandHandler> _logger;

    public AssignCreditCardCommandHandler(
        IUserRepository userRepository,
        ISavingsAccountRepository savingsAccountRepository,
        ICreditCardRepository creditCardRepository,
        IFinancialOperationRepository financialOperationRepository,
        INumberGenerator numberGenerator,
        ICardSecurityService cardSecurityService,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<AssignCreditCardCommandHandler> logger
    ) {
        _userRepository = userRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _creditCardRepository = creditCardRepository;
        _financialOperationRepository = financialOperationRepository;
        _numberGenerator = numberGenerator;
        _cardSecurityService = cardSecurityService;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _currentUser = currentUser;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<AssignCreditCardResponse>> Handle(
        AssignCreditCardCommand message,
        CancellationToken cancellationToken
    ) {
        // El cliente debe existir, estar activo y pertenecer al rol Cliente.
        var customer = await _userRepository.GetByIdAsync(
            message.CustomerUserId,
            cancellationToken
        );
        if (customer is null) {
            return Result.Failure<AssignCreditCardResponse>(
                DomainError.NotFound(
                    "Card.CustomerNotFound",
                    "No existe un cliente con este identificador."
                )
            );
        }

        if (!customer.IsActive) {
            return Result.Failure<AssignCreditCardResponse>(
                DomainError.Conflict(
                    "Card.CustomerNotActive",
                    "El cliente está inactivo y no puede recibir una tarjeta de crédito."
                )
            );
        }

        var roles = await _userRepository.GetRolesAsync(
            message.CustomerUserId,
            cancellationToken
        );
        if (!roles.Contains(nameof(Roles.Cliente))) {
            return Result.Failure<AssignCreditCardResponse>(
                DomainError.Conflict(
                    "Card.CustomerNotClient",
                    "El identificador no corresponde a un cliente con rol Cliente."
                )
            );
        }

        // El cliente debe tener una cuenta principal activa.
        var principalAccount = await _savingsAccountRepository.GetPrincipalByOwnerAsync(
            message.CustomerUserId,
            cancellationToken
        );
        if (principalAccount is null || principalAccount.Status != AccountStatus.Active) {
            return Result.Failure<AssignCreditCardResponse>(
                DomainError.PreconditionFailed(
                    "Card.NoPrincipalAccount",
                    "El cliente no tiene una cuenta de ahorro principal activa."
                )
            );
        }

        // Límite válido e importes de la operación.
        var limitResult = Money.Create(message.CreditLimit);
        if (limitResult.IsFailure) {
            return Result.Failure<AssignCreditCardResponse>(limitResult.Error!);
        }

        // Número único de 16 dígitos (BIN propio + secuencia + Luhn) y huella.
        string pan = await _numberGenerator.NextCardNumberAsync(cancellationToken);
        string lastFour = pan[^4..];
        string panFingerprint = _cardSecurityService.ComputePanFingerprint(pan);

        // CVC aleatorio de 3 dígitos; solo se persiste su digest.
        string cvc = RandomNumberGenerator.GetInt32(0, 1000).ToString(
            "D3",
            CultureInfo.InvariantCulture
        );
        var cvcDigestResult = CvcDigest.Create(
            _cardSecurityService.ComputeCvcDigest(cvc)
        );
        if (cvcDigestResult.IsFailure) {
            return Result.Failure<AssignCreditCardResponse>(cvcDigestResult.Error!);
        }

        var issuedAt = _clock.Now;
        var businessDate = _clock.Today;

        // El dominio valida titular, montos y fecha de emisión.
        var cardResult = CreditCardEntity.Issue(
            customer.Id,
            lastFour,
            panFingerprint,
            cvcDigestResult.Value,
            limitResult.Value,
            _currentUser.UserId!,
            issuedAt,
            businessDate
        );
        if (cardResult.IsFailure) {
            return Result.Failure<AssignCreditCardResponse>(cardResult.Error!);
        }

        var card = cardResult.Value;

        // Persistir tarjeta y operación de historial atómicamente.
        // La tarjeta aún no tiene identidad persistida, por lo que la operación
        // de asignación no referencia CreditCardId (decisión registrada).
        var persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                await _creditCardRepository.AddAsync(card, ct);

                var operationResult = FinancialOperation.Approve(
                    Guid.NewGuid(),
                    FinancialOperationKind.CardAssigned,
                    Money.Zero,
                    Money.Zero,
                    Money.Zero,
                    _currentUser.UserId!,
                    issuedAt,
                    []
                );
                if (operationResult.IsFailure) {
                    return Result.Failure(operationResult.Error!);
                }

                await _financialOperationRepository.AddAsync(operationResult.Value, ct);
                return Result.Success();
            },
            ct: cancellationToken
        );
        if (persistResult.IsFailure) {
            return Result.Failure<AssignCreditCardResponse>(persistResult.Error!);
        }

        // Enviar correo post-commit (fallo no revierte la asignación).
        string? notificationWarning = await SendAssignedEmailAsync(
            customer,
            card,
            CancellationToken.None
        );

        return Result.Success(
            new AssignCreditCardResponse(
                card.Id,
                $"************{card.LastFour}",
                card.LastFour,
                customer.Id,
                $"{customer.FirstName} {customer.LastName}".Trim(),
                card.CreditLimit.Amount,
                card.AvailableCredit.Amount,
                card.Expiration.ToString(),
                card.Status.ToString(),
                card.IssuedAt,
                notificationWarning
            )
        );
    }

    private async Task<string?> SendAssignedEmailAsync(
        UserListDto customer,
        CreditCardEntity card,
        CancellationToken cancellationToken
    ) {
        try {
            await _emailService.SendAsync(
                    customer.Email,
                    new CardAssignedModel(
                        $"{customer.FirstName} {customer.LastName}".Trim(),
                        card.LastFour,
                        card.CreditLimit,
                        card.Expiration.ToString(),
                        card.IssuedAt,
                        _clock.BusinessTimeZone
                    ),
                cancellationToken
            );
            return null;
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo de tarjeta asignada para la tarjeta con terminación {LastFour}.",
                card.LastFour
            );
            return NotificationMessages.EmailFailed;
        }
    }
}
