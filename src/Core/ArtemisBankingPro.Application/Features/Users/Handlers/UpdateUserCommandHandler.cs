using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Users.Handlers;

/// <summary>
/// Actualiza los datos de un usuario sin cambiar su rol. Si el usuario es
/// Cliente o Comercio y se indica un monto adicional mayor que cero, el monto
/// se acredita a la cuenta de ahorro principal con una transacción CRÉDITO.
/// </summary>
public sealed class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, Result<Unit>> {
    private readonly IUserAccountService _userAccountService;
    private readonly IUserRepository _userRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;

    public UpdateUserCommandHandler(
        IUserAccountService userAccountService,
        IUserRepository userRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser
    ) {
        _userAccountService = userAccountService;
        _userRepository = userRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _financialOperationRepository = financialOperationRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async ValueTask<Result<Unit>> Handle(
        UpdateUserCommand message,
        CancellationToken cancellationToken
    ) {
        Result transactionResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                UserListDto? user = await _userRepository.GetByIdAsync(message.UserId, ct);
                if (user is null) {
                    return Result.Failure(
                        DomainError.NotFound("User.NotFound", "El usuario seleccionado no existe.")
                    );
                }

                if (
                    await _userRepository.ExistsByUserNameAsync(message.UserName, ct)
                    && !string.Equals(message.UserName, user.UserName, StringComparison.OrdinalIgnoreCase)
                ) {
                    return Result.Failure(
                        DomainError.Conflict(
                            "User.UserNameExists",
                            "Ya existe otro usuario registrado con este nombre de usuario."
                        )
                    );
                }

                if (
                    await _userRepository.ExistsByEmailAsync(message.Email, ct)
                    && !string.Equals(message.Email, user.Email, StringComparison.OrdinalIgnoreCase)
                ) {
                    return Result.Failure(
                        DomainError.Conflict(
                            "User.EmailExists",
                            "Ya existe otro usuario registrado con este correo electrónico."
                        )
                    );
                }

                if (
                    await _userRepository.ExistsByIdentityDocumentAsync(message.Identification, ct)
                    && !string.Equals(message.Identification, user.Identification, StringComparison.Ordinal)
                ) {
                    return Result.Failure(
                        DomainError.Conflict(
                            "User.IdentificationExists",
                            "Ya existe otro usuario registrado con esta cédula."
                        )
                    );
                }

                Result updateResult = await _userAccountService.UpdateUserProfileAsync(
                    message.UserId,
                    message.FirstName,
                    message.LastName,
                    message.Identification,
                    message.Email,
                    message.UserName,
                    ct
                );
                if (updateResult.IsFailure) {
                    return updateResult;
                }

                if (!string.IsNullOrWhiteSpace(message.Password)) {
                    Result passwordResult = await _userAccountService.ChangePasswordAsync(
                        message.UserId,
                        message.Password,
                        ct
                    );
                    if (passwordResult.IsFailure) {
                        return passwordResult;
                    }
                }

                if (message.AdditionalAmount is > 0m
                    && user.Role is nameof(Roles.Cliente) or nameof(Roles.Comercio)) {
                    return await ApplyAdditionalFundingAsync(
                        message.UserId,
                        message.AdditionalAmount.Value,
                        ct
                    );
                }

                return Result.Success();
            },
            ct: cancellationToken
        );

        return transactionResult.IsSuccess
            ? Result.Success(Unit.Value)
            : Result.Failure<Unit>(transactionResult.Error!);
    }

    private async Task<Result> ApplyAdditionalFundingAsync(
        string ownerUserId,
        decimal additionalAmount,
        CancellationToken cancellationToken
    ) {
        var principalAccount = await _savingsAccountRepository.GetPrincipalByOwnerAsync(
            ownerUserId,
            cancellationToken
        );
        if (principalAccount is null
            || principalAccount.Status != Domain.Accounts.Enums.AccountStatus.Active) {
            return Result.Failure(
                DomainError.PreconditionFailed(
                    "User.NoPrincipalAccount",
                    "El usuario no tiene una cuenta de ahorro principal activa."
                )
            );
        }

        var amount = Money.Create(additionalAmount);
        if (amount.IsFailure) {
            return Result.Failure(amount.Error!);
        }

        var occurredAt = _clock.Now;

        var creditResult = principalAccount.Credit(amount.Value);
        if (creditResult.IsFailure) {
            return creditResult;
        }

        _savingsAccountRepository.Update(principalAccount);

        var operationResult = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.AdministrativeFunding,
            amount.Value,
            amount.Value,
            Money.Zero,
            _currentUser.UserId!,
            occurredAt,
            [
                new AccountTransactionDetails(
                    principalAccount.Number,
                    TransactionDirection.Credit,
                    amount.Value,
                    "FONDEO_ADMINISTRATIVO",
                    principalAccount.Number.Value
                ),
            ]
        );
        if (operationResult.IsFailure) {
            return Result.Failure(operationResult.Error!);
        }

        await _financialOperationRepository.AddAsync(operationResult.Value, cancellationToken);
        return Result.Success();
    }
}
