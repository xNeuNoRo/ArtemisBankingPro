using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using Mediator;

namespace ArtemisBankingPro.Application.Features.CreditCard.Handlers;

/// <summary>
/// Cancela una tarjeta de crédito activa sin deuda y registra la operación de
/// historial <see cref="FinancialOperationKind.CardCancelled"/> de forma atómica.
/// La tarjeta cancelada rechaza consumos, avances y pagos futuros
/// (<see cref="CreditCardEntity.CanAuthorizeCharge"/>).
/// </summary>
public sealed class CancelCreditCardCommandHandler
    : IRequestHandler<CancelCreditCardCommand, Result<Unit>> {
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IBusinessClock _clock;

    public CancelCreditCardCommandHandler(
        ICreditCardRepository creditCardRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IBusinessClock clock
    ) {
        _creditCardRepository = creditCardRepository;
        _financialOperationRepository = financialOperationRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async ValueTask<Result<Unit>> Handle(
        CancelCreditCardCommand message,
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

        // 2. Estado y deuda validados por el dominio.
        Result cancelResult = card.Cancel(_clock.Now);
        if (cancelResult.IsFailure) {
            return Result.Failure<Unit>(cancelResult.Error!);
        }

        // 3. Persistir tarjeta y operación de historial atómicamente.
        var persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                _creditCardRepository.Update(card);

                var operationResult = FinancialOperation.Approve(
                    Guid.NewGuid(),
                    FinancialOperationKind.CardCancelled,
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

        return Result.Success(Unit.Value);
    }
}
