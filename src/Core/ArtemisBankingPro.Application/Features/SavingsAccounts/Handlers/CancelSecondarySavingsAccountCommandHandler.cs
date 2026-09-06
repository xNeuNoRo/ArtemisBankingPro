using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Handlers;

public sealed class CancelSecondarySavingsAccountCommandHandler
    : IRequestHandler<CancelSecondarySavingsAccountCommand, Result<Unit>> {
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IBusinessClock _clock;

    public CancelSecondarySavingsAccountCommandHandler(
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IBusinessClock clock
    ) {
        _savingsAccountRepository = savingsAccountRepository;
        _financialOperationRepository = financialOperationRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async ValueTask<Result<Unit>> Handle(
        CancelSecondarySavingsAccountCommand message,
        CancellationToken cancellationToken
    ) {
        Result<AccountNumber> numberResult = AccountNumber.Create(message.AccountNumber);
        if (numberResult.IsFailure) {
            return Result.Failure<Unit>(numberResult.Error!);
        }

        Result persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                SavingsAccount? secondary = await _savingsAccountRepository.GetByNumberAsync(
                    numberResult.Value,
                    ct
                );
                if (secondary is null) {
                    return Result.Failure(
                        DomainError.NotFound("Account.NotFound", "La cuenta de ahorro no existe.")
                    );
                }

                if (secondary.Status != AccountStatus.Active) {
                    return Result.Failure(
                        DomainError.Conflict("Account.NotActive", "La cuenta de ahorro no está activa.")
                    );
                }

                if (secondary.Type == AccountType.Primary) {
                    return Result.Failure(
                        DomainError.Conflict(
                            "Account.PrincipalCannotBeCancelled",
                            "La cuenta de ahorro principal no se puede cancelar."
                        )
                    );
                }

                SavingsAccount? principal = await _savingsAccountRepository.GetPrincipalByOwnerAsync(
                    secondary.OwnerUserId,
                    ct
                );
                if (principal is null) {
                    return Result.Failure(
                        DomainError.PreconditionFailed(
                            "Account.NoPrincipalAccount",
                            "El cliente no tiene una cuenta de ahorro principal activa."
                        )
                    );
                }

                DateTimeOffset cancelledAt = _clock.Now;
                FinancialOperation operation;
                if (secondary.Balance == Money.Zero) {
                    Result cancelResult = secondary.Cancel(cancelledAt);
                    if (cancelResult.IsFailure) {
                        return cancelResult;
                    }

                    Result<FinancialOperation> operationResult = FinancialOperation.Approve(
                        Guid.NewGuid(),
                        FinancialOperationKind.AccountCancelled,
                        Money.Zero,
                        Money.Zero,
                        Money.Zero,
                        _currentUser.UserId!,
                        cancelledAt,
                        [],
                        savingsAccountId: secondary.Id
                    );
                    if (operationResult.IsFailure) {
                        return Result.Failure(operationResult.Error!);
                    }

                    operation = operationResult.Value;
                }
                else {
                    Result<CancellationTransfer> transferResult =
                        secondary.CancelWithBalanceTransfer(principal, cancelledAt);
                    if (transferResult.IsFailure) {
                        return Result.Failure(transferResult.Error!);
                    }

                    CancellationTransfer transfer = transferResult.Value;
                    Result<FinancialOperation> operationResult = FinancialOperation.Approve(
                        Guid.NewGuid(),
                        FinancialOperationKind.SecondaryAccountClosureTransfer,
                        transfer.TransferredAmount,
                        transfer.TransferredAmount,
                        Money.Zero,
                        _currentUser.UserId!,
                        cancelledAt,
                        transfer.Transactions,
                        savingsAccountId: secondary.Id
                    );
                    if (operationResult.IsFailure) {
                        return Result.Failure(operationResult.Error!);
                    }

                    operation = operationResult.Value;
                    _savingsAccountRepository.Update(principal);
                }

                _savingsAccountRepository.Update(secondary);
                await _financialOperationRepository.AddAsync(operation, ct);
                return Result.Success();
            },
            ct: cancellationToken
        );

        return persistResult.IsSuccess
            ? Result.Success(Unit.Value)
            : Result.Failure<Unit>(persistResult.Error!);
    }
}
