using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.CreditCard.Handlers;

/// <summary>
/// Modifica el límite de crédito de una tarjeta activa y registra la operación
/// de historial <see cref="FinancialOperationKind.CardLimitChanged"/> de forma
/// atómica. El correo de notificación se envía después del commit y su fallo no
/// revierte el cambio.
/// </summary>
public sealed class UpdateCardLimitCommandHandler
    : IRequestHandler<UpdateCardLimitCommand, Result<Unit>> {
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IBusinessClock _clock;
    private readonly IEmailService _emailService;
    private readonly ILogger<UpdateCardLimitCommandHandler> _logger;

    public UpdateCardLimitCommandHandler(
        ICreditCardRepository creditCardRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IBusinessClock clock,
        IEmailService emailService,
        ILogger<UpdateCardLimitCommandHandler> logger
    ) {
        _creditCardRepository = creditCardRepository;
        _financialOperationRepository = financialOperationRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<Unit>> Handle(
        UpdateCardLimitCommand message,
        CancellationToken cancellationToken
    ) {
        // 1. La tarjeta debe existir.
        CreditCardEntity? card = await _creditCardRepository.GetByIdAsync(
            message.CardId,
            cancellationToken
        );
        if (card is null) {
            return Result.Failure<Unit>(
                DomainError.NotFound(
                    "Card.NotFound",
                    "La tarjeta de crédito seleccionada no existe."
                )
            );
        }

        // 2. Nuevo límite válido como importe.
        Result<Money> moneyResult = Money.Create(message.NewLimit);
        if (moneyResult.IsFailure) {
            return Result.Failure<Unit>(moneyResult.Error!);
        }

        // 3. Dominio valida estado, límite positivo y deuda actual.
        Result changeResult = card.ChangeCreditLimit(moneyResult.Value);
        if (changeResult.IsFailure) {
            return Result.Failure<Unit>(changeResult.Error!);
        }

        // 4. Persistir tarjeta y operación de historial atómicamente.
        var persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                _creditCardRepository.Update(card);

                var operationResult = FinancialOperation.Approve(
                    Guid.NewGuid(),
                    FinancialOperationKind.CardLimitChanged,
                    Money.Zero,
                    Money.Zero,
                    Money.Zero,
                    _currentUser.UserId!,
                    _clock.Now,
                    [],
                    creditCardId: message.CardId
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
            return Result.Failure<Unit>(persistResult.Error!);
        }

        // 5. Correo post-commit (fallo no revierte el cambio).
        await SendLimitChangedEmailAsync(card, moneyResult.Value, _clock.Now, cancellationToken);

        return Result.Success(Unit.Value);
    }

    private async Task SendLimitChangedEmailAsync(
        CreditCardEntity card,
        Money newLimit,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken
    ) {
        var customer = await _userRepository.GetByIdAsync(
            card.CustomerUserId,
            cancellationToken
        );
        if (customer is null) {
            return;
        }

        try {
            await _emailService.SendAsync(
                customer.Email,
                new CardLimitChangedModel(
                    $"{customer.FirstName} {customer.LastName}".Trim(),
                    card.LastFour,
                    newLimit,
                    modifiedAt,
                    _clock.BusinessTimeZone
                ),
                cancellationToken
            );
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo de cambio de límite para la tarjeta con terminación {LastFour}.",
                card.LastFour
            );
        }
    }
}
