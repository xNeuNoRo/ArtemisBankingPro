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
/// Implementa <see cref="ITransferProcessor"/>: único guardián de las
/// invariantes mutables de la transferencia (estado, fondos, capacidad de
/// crédito) y de la escritura atómica débito + crédito + transacciones
/// pareadas + operación, con actualización en orden estable (Id ascendente).
/// </summary>
public sealed class TransferProcessor : ITransferProcessor {
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;

    public TransferProcessor(
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

    public async Task<Result<FinancialOperationOutcome>> TransferAsync(
        SavingsAccount source,
        SavingsAccount destination,
        Money amount,
        TransferFlow flow,
        string initiatedByUserId,
        CancellationToken ct = default
    ) {
        if (source.Status != AccountStatus.Active || destination.Status != AccountStatus.Active) {
            return Result.Failure<FinancialOperationOutcome>(AccountErrors.NotActive);
        }

        Result canDebit = source.CanDebit(amount);
        if (canDebit.IsFailure) {
            Result rejection = await PersistRejectionAsync(
                source,
                destination,
                amount,
                flow,
                canDebit.Error!,
                initiatedByUserId,
                ct
            );
            if (rejection.IsFailure) {
                return Result.Failure<FinancialOperationOutcome>(rejection.Error!);
            }

            return Result.Failure<FinancialOperationOutcome>(canDebit.Error!);
        }

        Result canCredit = destination.CanCredit(amount);
        if (canCredit.IsFailure) {
            return Result.Failure<FinancialOperationOutcome>(canCredit.Error!);
        }

        FinancialOperationKind kind = KindFor(flow);
        Guid operationId = Guid.NewGuid();
        DateTimeOffset occurredAt = _clock.Now;

        Result persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async token => {
                Result debitResult = source.Debit(amount);
                if (debitResult.IsFailure) {
                    return debitResult;
                }

                Result creditResult = destination.Credit(amount);
                if (creditResult.IsFailure) {
                    return creditResult;
                }

                foreach (
                    SavingsAccount account in new[] { source, destination }.OrderBy(
                        account => account.Id
                    )
                ) {
                    _savingsAccountRepository.Update(account);
                }

                Result<FinancialOperation> operationResult = FinancialOperation.Approve(
                    operationId,
                    kind,
                    amount,
                    amount,
                    Money.Zero,
                    initiatedByUserId,
                    occurredAt,
                    [
                        new AccountTransactionDetails(
                            source.Number,
                            TransactionDirection.Debit,
                            amount,
                            source.Number.Value,
                            destination.Number.Value
                        ),
                        new AccountTransactionDetails(
                            destination.Number,
                            TransactionDirection.Credit,
                            amount,
                            source.Number.Value,
                            destination.Number.Value
                        ),
                    ]
                );
                if (operationResult.IsFailure) {
                    return Result.Failure(operationResult.Error!);
                }

                FinancialOperation operation = operationResult.Value;
                if (flow == TransferFlow.CashierThirdParty) {
                    operation.RecordThirdPartyTransferProcessed(
                        source.Number.Value,
                        destination.Number.Value,
                        amount,
                        source.OwnerUserId,
                        destination.OwnerUserId,
                        initiatedByUserId
                    );
                }

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
        SavingsAccount source,
        SavingsAccount destination,
        Money amount,
        TransferFlow flow,
        DomainError error,
        string initiatedByUserId,
        CancellationToken ct
    ) {
        Result<FinancialOperation> operation = FinancialOperation.Reject(
            Guid.NewGuid(),
            KindFor(flow),
            amount,
            Money.Zero,
            initiatedByUserId,
            _clock.Now,
            error.Code,
            [
                new AccountTransactionDetails(
                    source.Number,
                    TransactionDirection.Debit,
                    amount,
                    source.Number.Value,
                    destination.Number.Value
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

    private static FinancialOperationKind KindFor(TransferFlow flow) =>
        flow switch {
            TransferFlow.CashierThirdParty => FinancialOperationKind.CashierTransfer,
            TransferFlow.OwnAccounts => FinancialOperationKind.OwnAccountTransfer,
            TransferFlow.Beneficiary => FinancialOperationKind.BeneficiaryTransfer,
            TransferFlow.Express => FinancialOperationKind.ExpressTransfer,
            _ => throw new ArgumentOutOfRangeException(nameof(flow), flow, null),
        };
}
