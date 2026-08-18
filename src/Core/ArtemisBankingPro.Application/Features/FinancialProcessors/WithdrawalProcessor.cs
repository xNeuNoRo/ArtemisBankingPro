using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;

namespace ArtemisBankingPro.Application.Features.FinancialProcessors;

/// <summary>
/// Implementa <see cref="IWithdrawalProcessor"/>: único guardián de las
/// invariantes mutables del retiro (estado, fondos) y de la escritura atómica
/// débito + operación. El registro financiero usa Origen = número de la
/// cuenta y Beneficiario = "RETIRO" (spec §28).
/// </summary>
public sealed class WithdrawalProcessor : IWithdrawalProcessor {
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;

    public WithdrawalProcessor(
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock
    ) {
        _savingsAccountRepository = savingsAccountRepository;
        _financialOperationRepository = financialOperationRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<FinancialOperationOutcome>> WithdrawAsync(
        SavingsAccount account,
        Money amount,
        string initiatedByUserId,
        CancellationToken ct = default
    ) {
        if (account.Status != AccountStatus.Active) {
            return Result.Failure<FinancialOperationOutcome>(AccountErrors.NotActive);
        }

        Result canDebit = account.CanDebit(amount);
        if (canDebit.IsFailure) {
            Result rejection = await PersistRejectionAsync(
                account,
                amount,
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
                Result debitResult = account.Debit(amount);
                if (debitResult.IsFailure) {
                    return debitResult;
                }

                _savingsAccountRepository.Update(account);

                Result<FinancialOperation> operationResult = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.Withdrawal,
                    amount,
                    amount,
                    Money.Zero,
                    initiatedByUserId,
                    occurredAt,
                    [
                        new AccountTransactionDetails(
                            account.Number,
                            TransactionDirection.Debit,
                            amount,
                            account.Number.Value,
                            "RETIRO"
                        ),
                    ]
                );
                if (operationResult.IsFailure) {
                    return Result.Failure(operationResult.Error!);
                }

                FinancialOperation operation = operationResult.Value;
                operation.RecordWithdrawalProcessed(
                    account.Number.Value,
                    amount,
                    account.OwnerUserId,
                    initiatedByUserId
                );

                await _financialOperationRepository.AddAsync(operation, token);
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
                amount,
                amount,
                Money.Zero,
                occurredAt
            )
        );
    }

    private async Task<Result> PersistRejectionAsync(
        SavingsAccount account,
        Money amount,
        DomainError error,
        string initiatedByUserId,
        CancellationToken ct
    ) {
        Result<FinancialOperation> operation = FinancialOperation.Reject(
            Guid.NewGuid(),
            FinancialOperationKind.Withdrawal,
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
                    "RETIRO"
                ),
            ]
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
