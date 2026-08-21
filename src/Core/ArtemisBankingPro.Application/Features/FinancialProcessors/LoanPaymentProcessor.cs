using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using ArtemisBankingPro.Domain.Lending.Errors;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;

namespace ArtemisBankingPro.Application.Features.FinancialProcessors;

/// <summary>
/// Implementa <see cref="ILoanPaymentProcessor"/>: único guardián de las
/// invariantes mutables del pago a préstamo (estado, pendiente, fondos) y de
/// la escritura atómica débito + aplicación a cuotas + operación.
/// </summary>
public sealed class LoanPaymentProcessor : ILoanPaymentProcessor {
    private readonly ILoanRepository _loanRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;

    public LoanPaymentProcessor(
        ILoanRepository loanRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock
    ) {
        _loanRepository = loanRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _financialOperationRepository = financialOperationRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<FinancialOperationOutcome>> PayAsync(
        Loan loan,
        SavingsAccount account,
        Money requestedAmount,
        string initiatedByUserId,
        CancellationToken ct = default
    ) {
        if (loan.Status != LoanStatus.Active) {
            return await RejectAsync(loan, account, requestedAmount, LoanErrors.NotActive, initiatedByUserId, ct);
        }

        if (loan.OutstandingAmount == Money.Zero) {
            return await RejectAsync(loan, account, requestedAmount, LoanErrors.NoPendingInstallments, initiatedByUserId, ct);
        }

        if (account.Status != AccountStatus.Active) {
            return await RejectAsync(loan, account, requestedAmount, AccountErrors.NotActive, initiatedByUserId, ct);
        }

        Money effectiveAmount = Money.Create(
            Math.Min(requestedAmount.Amount, loan.OutstandingAmount.Amount)
        ).Value;

        Result canDebit = account.CanDebit(effectiveAmount);
        if (canDebit.IsFailure) {
            Result rejection = await PersistRejectionAsync(
                loan,
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

                Result<Money> paymentResult = loan.ApplyPayment(effectiveAmount, occurredAt);
                if (paymentResult.IsFailure) {
                    return Result.Failure(paymentResult.Error!);
                }

                _savingsAccountRepository.Update(account);
                _loanRepository.Update(loan);

                Result<FinancialOperation> operationResult = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.LoanPayment,
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
                            loan.Number.Value
                        ),
                    ],
                    loanNumber: loan.Number
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
        Loan loan,
        SavingsAccount account,
        Money amount,
        DomainError error,
        string initiatedByUserId,
        CancellationToken ct
    ) {
        Result<FinancialOperation> operation = FinancialOperation.Reject(
            Guid.NewGuid(),
            FinancialOperationKind.LoanPayment,
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
                    loan.Number.Value
                ),
            ],
            loanNumber: loan.Number
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

    private async Task<Result<FinancialOperationOutcome>> RejectAsync(
        Loan loan,
        SavingsAccount account,
        Money amount,
        DomainError error,
        string initiatedByUserId,
        CancellationToken ct
    ) {
        Result rejection = await PersistRejectionAsync(
            loan,
            account,
            amount,
            error,
            initiatedByUserId,
            ct
        );
        return rejection.IsFailure
            ? Result.Failure<FinancialOperationOutcome>(rejection.Error!)
            : Result.Failure<FinancialOperationOutcome>(error);
    }
}
