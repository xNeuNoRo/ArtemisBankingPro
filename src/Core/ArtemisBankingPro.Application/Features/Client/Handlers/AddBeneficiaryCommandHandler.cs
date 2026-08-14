using System.Data;
using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class AddBeneficiaryCommandHandler
    : IRequestHandler<AddBeneficiaryCommand, Result<Unit>> {
    private static readonly DomainError OwnAccount = DomainError.Validation(
        "Beneficiary.OwnAccount",
        "No puede agregar una cuenta propia como beneficiario."
    );
    private static readonly DomainError AlreadyExists = DomainError.Conflict(
        "Beneficiary.AlreadyExists",
        "Esta cuenta ya se encuentra registrada como beneficiario."
    );

    private readonly IBeneficiaryRepository _beneficiaryRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IBusinessClock _clock;

    public AddBeneficiaryCommandHandler(
        IBeneficiaryRepository beneficiaryRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IBusinessClock clock
    ) {
        _beneficiaryRepository = beneficiaryRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async ValueTask<Result<Unit>> Handle(
        AddBeneficiaryCommand message,
        CancellationToken cancellationToken
    ) {
        Result<AccountNumber> numberResult = AccountNumber.Create(
            message.DestinationAccountNumber
        );
        if (numberResult.IsFailure) {
            return Result.Failure<Unit>(numberResult.Error!);
        }

        Result result = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                SavingsAccount? destination =
                    await _savingsAccountRepository.GetByNumberAsync(
                        numberResult.Value,
                        ct
                    );
                if (destination is null) {
                    return Result.Failure(AccountErrors.DestinationNotFound);
                }

                if (destination.Status != AccountStatus.Active) {
                    return Result.Failure(AccountErrors.NotActive);
                }

                string ownerUserId = _currentUser.UserId!;
                if (destination.OwnerUserId == ownerUserId) {
                    return Result.Failure(OwnAccount);
                }

                if (
                    await _beneficiaryRepository.ExistsAsync(
                        ownerUserId,
                        destination.Id,
                        ct
                    )
                ) {
                    return Result.Failure(AlreadyExists);
                }

                Result<Beneficiary> beneficiaryResult = Beneficiary.Create(
                    ownerUserId,
                    destination.Id,
                    _clock.Now
                );
                if (beneficiaryResult.IsFailure) {
                    return Result.Failure(beneficiaryResult.Error!);
                }

                await _beneficiaryRepository.AddAsync(beneficiaryResult.Value, ct);
                return Result.Success();
            },
            IsolationLevel.Serializable,
            cancellationToken
        );

        return result.IsSuccess
            ? Result.Success(Unit.Value)
            : Result.Failure<Unit>(result.Error!);
    }
}
