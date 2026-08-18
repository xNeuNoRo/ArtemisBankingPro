using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.Application.Features.FinancialProcessors;

/// <summary>
/// Implementa <see cref="ICardPaymentProcessor"/>: único guardián de las
/// invariantes mutables del pago a tarjeta (estado, deuda, fondos) y de la
/// escritura atómica débito + reducción de deuda + operación.
/// </summary>
public sealed class CardPaymentProcessor : ICardPaymentProcessor {
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;

    public CardPaymentProcessor(
        ICreditCardRepository creditCardRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock
    ) {
        _creditCardRepository = creditCardRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _financialOperationRepository = financialOperationRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<FinancialOperationOutcome>> PayAsync(
        CreditCardEntity card,
        SavingsAccount account,
        Money requestedAmount,
        string initiatedByUserId,
        CancellationToken ct = default
    ) {
        if (card.Status != CreditCardStatus.Active) {
            return Result.Failure<FinancialOperationOutcome>(CardErrors.NotActive);
        }

        if (card.CurrentDebt == Money.Zero) {
            return Result.Failure<FinancialOperationOutcome>(CardErrors.NoDebt);
        }

        if (account.Status != AccountStatus.Active) {
            return Result.Failure<FinancialOperationOutcome>(AccountErrors.NotActive);
        }

        Money effectiveAmount = Money.Create(
            Math.Min(requestedAmount.Amount, card.CurrentDebt.Amount)
        ).Value;

        Result canDebit = account.CanDebit(effectiveAmount);
        if (canDebit.IsFailure) {
            Result rejection = await PersistRejectionAsync(
                card,
                account,
                effectiveAmount,
                canDebit.Error!,
                initiatedByUserId,
                ct
            );
            if (rejection.IsFailure) {
                return Result.Failure<FinancialOperationOutcome>(rejection.Error!);
            }

            return Result.Failure<FinancialOperationOutcome>(canDebit.Error!);
        }

        Guid operationId = Guid.NewGuid();
        DateTimeOffset occurredAt = _clock.Now;

        Result persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async token => {
                Result debitResult = account.Debit(effectiveAmount);
                if (debitResult.IsFailure) {
                    return debitResult;
                }

                Result<Money> paymentResult = card.ApplyPayment(effectiveAmount, occurredAt);
                if (paymentResult.IsFailure) {
                    return Result.Failure(paymentResult.Error!);
                }

                _savingsAccountRepository.Update(account);
                _creditCardRepository.Update(card);

                Result<FinancialOperation> operationResult = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.CreditCardPayment,
                    requestedAmount,
                    effectiveAmount,
                    Money.Zero,
                    initiatedByUserId,
                    occurredAt,
                    [
                        new AccountTransactionDetails(
                            account.Number,
                            TransactionDirection.Debit,
                            effectiveAmount,
                            account.Number.Value,
                            card.LastFour
                        ),
                    ],
                    creditCardId: card.Id
                );
                if (operationResult.IsFailure) {
                    return Result.Failure(operationResult.Error!);
                }

                await _financialOperationRepository.AddAsync(operationResult.Value, token);
                return Result.Success();
            },
            ct: ct
        );
        if (persistResult.IsFailure) {
            return Result.Failure<FinancialOperationOutcome>(persistResult.Error!);
        }

        return Result.Success(
            new FinancialOperationOutcome(
                operationId,
                requestedAmount,
                effectiveAmount,
                Money.Zero,
                occurredAt
            )
        );
    }

    private async Task<Result> PersistRejectionAsync(
        CreditCardEntity card,
        SavingsAccount account,
        Money amount,
        DomainError error,
        string initiatedByUserId,
        CancellationToken ct
    ) {
        Result<FinancialOperation> operation = FinancialOperation.Reject(
            Guid.NewGuid(),
            FinancialOperationKind.CreditCardPayment,
            amount,
            Money.Zero,
            initiatedByUserId,
            _clock.Now,
            error.Code,
            [
                new AccountTransactionDetails(
                    account.Number,
                    TransactionDirection.Debit,
                    amount,
                    account.Number.Value,
                    card.LastFour
                ),
            ],
            creditCardId: card.Id
        );
        if (operation.IsFailure) {
            return Result.Failure(operation.Error!);
        }

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token => {
                await _financialOperationRepository.AddAsync(operation.Value, token);
                return Result.Success();
            },
            ct: ct
        );
    }
}
