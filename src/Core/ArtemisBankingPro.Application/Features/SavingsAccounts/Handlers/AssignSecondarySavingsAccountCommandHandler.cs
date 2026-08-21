using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Handlers;

public sealed class AssignSecondarySavingsAccountCommandHandler
    : IRequestHandler<AssignSecondarySavingsAccountCommand, Result<SavingsAccountResponse>> {
    private readonly IUserRepository _userRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly INumberGenerator _numberGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;

    public AssignSecondarySavingsAccountCommandHandler(
        IUserRepository userRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        INumberGenerator numberGenerator,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser
    ) {
        _userRepository = userRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _financialOperationRepository = financialOperationRepository;
        _numberGenerator = numberGenerator;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async ValueTask<Result<SavingsAccountResponse>> Handle(
        AssignSecondarySavingsAccountCommand message,
        CancellationToken cancellationToken
    ) {
        var customer = await _userRepository.GetByIdAsync(
            message.CustomerUserId,
            cancellationToken
        );
        if (customer is null) {
            return Result.Failure<SavingsAccountResponse>(
                DomainError.NotFound(
                    "Account.CustomerNotFound",
                    "No existe un cliente con este identificador."
                )
            );
        }

        if (!customer.IsActive) {
            return Result.Failure<SavingsAccountResponse>(
                DomainError.Conflict(
                    "Account.CustomerNotActive",
                    "El cliente está inactivo y no puede recibir una cuenta secundaria."
                )
            );
        }

        var roles = await _userRepository.GetRolesAsync(
            message.CustomerUserId,
            cancellationToken
        );
        if (!roles.Contains(nameof(Roles.Cliente))) {
            return Result.Failure<SavingsAccountResponse>(
                DomainError.Conflict(
                    "Account.CustomerNotClient",
                    "El identificador no corresponde a un cliente con rol Cliente."
                )
            );
        }

        var principalAccount = await _savingsAccountRepository.GetPrincipalByOwnerAsync(
            message.CustomerUserId,
            cancellationToken
        );
        if (principalAccount is null || principalAccount.Status != AccountStatus.Active) {
            return Result.Failure<SavingsAccountResponse>(
                DomainError.PreconditionFailed(
                    "Account.NoPrincipalAccount",
                    "El cliente no tiene una cuenta de ahorro principal activa."
                )
            );
        }

        if (message.InitialAmount < 0m) {
            return Result.Failure<SavingsAccountResponse>(
                DomainError.Validation(
                    "Account.InitialAmountMustNotBeNegative",
                    "El monto inicial no puede ser negativo."
                )
            );
        }

        var balanceResult = Money.Create(message.InitialAmount);
        if (balanceResult.IsFailure) {
            return Result.Failure<SavingsAccountResponse>(balanceResult.Error!);
        }

        var openedAt = _clock.Now;
        SavingsAccount? account = null;
        FinancialOperation? initialFunding = null;

        Result persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                string rawNumber = await _numberGenerator.NextAccountNumberAsync(ct);
                var numberResult = AccountNumber.Create(rawNumber);
                if (numberResult.IsFailure) {
                    return Result.Failure(numberResult.Error!);
                }

                var accountResult = SavingsAccount.OpenSecondary(
                    customer.Id,
                    numberResult.Value,
                    balanceResult.Value,
                    _currentUser.UserId!,
                    openedAt
                );
                if (accountResult.IsFailure) {
                    return Result.Failure(accountResult.Error!);
                }

                SavingsAccount createdAccount = accountResult.Value;
                account = createdAccount;
                if (balanceResult.Value != Money.Zero) {
                    var operationResult = FinancialOperation.Approve(
                        Guid.NewGuid(),
                        FinancialOperationKind.InitialFunding,
                        balanceResult.Value,
                        balanceResult.Value,
                        Money.Zero,
                        _currentUser.UserId!,
                        openedAt,
                        [
                            new AccountTransactionDetails(
                                createdAccount.Number,
                                TransactionDirection.Credit,
                                balanceResult.Value,
                                "APERTURA_CUENTA_SECUNDARIA",
                                createdAccount.Number.Value
                            ),
                        ]
                    );
                    if (operationResult.IsFailure) {
                        return Result.Failure(operationResult.Error!);
                    }

                    initialFunding = operationResult.Value;
                }

                await _savingsAccountRepository.AddAsync(createdAccount, ct);

                if (initialFunding is not null) {
                    await _financialOperationRepository.AddAsync(initialFunding, ct);
                }

                return Result.Success();
            },
            ct: cancellationToken
        );
        if (persistResult.IsFailure) {
            return Result.Failure<SavingsAccountResponse>(persistResult.Error!);
        }

        SavingsAccount persistedAccount = account
            ?? throw new InvalidOperationException("La cuenta no fue creada durante la transacción.");

        return Result.Success(
            new SavingsAccountResponse(
                persistedAccount.Id,
                persistedAccount.Number.Value,
                customer.Id,
                $"{customer.FirstName} {customer.LastName}".Trim(),
                persistedAccount.Balance.Amount,
                persistedAccount.Type.ToString(),
                persistedAccount.Status.ToString(),
                persistedAccount.OpenedAt
            )
        );
    }
}
