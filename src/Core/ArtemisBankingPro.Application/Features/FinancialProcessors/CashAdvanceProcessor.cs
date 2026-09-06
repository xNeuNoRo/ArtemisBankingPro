using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Cards.Policies;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.Application.Features.FinancialProcessors;

/// <summary>
/// Implementa <see cref="ICashAdvanceProcessor"/>: único guardián de las
/// invariantes del avance (cargo total, crédito disponible, estado de la
/// tarjeta/cuenta) y de la escritura atómica carga + crédito + consumo
/// <c>AVANCE</c> + operación.
/// </summary>
public sealed class CashAdvanceProcessor : ICashAdvanceProcessor {
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;

    public CashAdvanceProcessor(
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

    public async Task<Result<FinancialOperationOutcome>> AdvanceAsync(
        CreditCardEntity card,
        SavingsAccount destinationAccount,
        Money principal,
        string initiatedByUserId,
        CancellationToken ct = default
    ) {
        Result<CashAdvanceQuote> quoteResult = CashAdvancePolicy.Calculate(principal);
        if (quoteResult.IsFailure) {
            return Result.Failure<FinancialOperationOutcome>(quoteResult.Error!);
        }

        CashAdvanceQuote quote = quoteResult.Value;

        if (destinationAccount.Status != AccountStatus.Active) {
            return Result.Failure<FinancialOperationOutcome>(AccountErrors.NotActive);
        }

        Result canCharge = card.CanAuthorizeCharge(quote.TotalCardCharge, _clock.Today);
        if (canCharge.IsFailure) {
            if (canCharge.Error == CardErrors.InsufficientCredit) {
                Result rejection = await PersistRejectionAsync(
                    card,
                    quote,
                    canCharge.Error,
                    initiatedByUserId,
                    ct
                );
                if (rejection.IsFailure) {
                    return Result.Failure<FinancialOperationOutcome>(rejection.Error!);
                }
            }

            return Result.Failure<FinancialOperationOutcome>(canCharge.Error!);
        }

        Result canCredit = destinationAccount.CanCredit(quote.Principal);
        if (canCredit.IsFailure) {
            return Result.Failure<FinancialOperationOutcome>(canCredit.Error!);
        }

        Guid operationId = Guid.NewGuid();
        DateTimeOffset occurredAt = _clock.Now;

        Result persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async token => {
                var consumption = new CardConsumptionDetails(
                    card.Id,
                    null,
                    "AVANCE",
                    ConsumptionType.CashAdvance,
                    quote.TotalCardCharge
                );
                Result<FinancialOperation> operationResult = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.CashAdvance,
                    quote.Principal,
                    quote.Principal,
                    quote.Interest,
                    initiatedByUserId,
                    occurredAt,
                    [
                        new AccountTransactionDetails(
                            destinationAccount.Number,
                            TransactionDirection.Credit,
                            quote.Principal,
                            card.LastFour,
                            destinationAccount.Number.Value
                        ),
                    ],
                    consumption,
                    creditCardId: card.Id
                );
                if (operationResult.IsFailure) {
                    return Result.Failure(operationResult.Error!);
                }

                Result chargeResult = card.AuthorizeCharge(quote.TotalCardCharge, _clock.Today);
                if (chargeResult.IsFailure) {
                    return chargeResult;
                }

                Result creditResult = destinationAccount.Credit(quote.Principal);
                if (creditResult.IsFailure) {
                    return creditResult;
                }

                _creditCardRepository.Update(card);
                _savingsAccountRepository.Update(destinationAccount);
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
                quote.Principal,
                quote.Principal,
                quote.Interest,
                occurredAt
            )
        );
    }

    private async Task<Result> PersistRejectionAsync(
        CreditCardEntity card,
        CashAdvanceQuote quote,
        DomainError error,
        string initiatedByUserId,
        CancellationToken ct
    ) {
        Result<FinancialOperation> operation = FinancialOperation.Reject(
            Guid.NewGuid(),
            FinancialOperationKind.CashAdvance,
            quote.Principal,
            quote.Interest,
            initiatedByUserId,
            _clock.Now,
            error.Code,
            [],
            new CardConsumptionDetails(
                card.Id,
                null,
                "AVANCE",
                ConsumptionType.CashAdvance,
                quote.TotalCardCharge
            ),
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
