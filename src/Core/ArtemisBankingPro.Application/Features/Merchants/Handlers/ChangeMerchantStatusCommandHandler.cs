using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Merchants.Handlers;

/// <summary>
/// Activa o inactiva un comercio (spec §40, PATCH /api/commerce/{id}/status).
/// Al desactivar, el usuario asociado queda inactivo; al reactivar, los
/// usuarios asociados no se activan automáticamente.
/// </summary>
public sealed class ChangeMerchantStatusCommandHandler
    : IRequestHandler<ChangeMerchantStatusCommand, Result<Unit>> {
    private readonly IMerchantRepository _merchantRepository;
    private readonly IUserAccountService _userAccountService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;

    public ChangeMerchantStatusCommandHandler(
        IMerchantRepository merchantRepository,
        IUserAccountService userAccountService,
        IUnitOfWork unitOfWork,
        IBusinessClock clock
    ) {
        _merchantRepository = merchantRepository;
        _userAccountService = userAccountService;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async ValueTask<Result<Unit>> Handle(
        ChangeMerchantStatusCommand message,
        CancellationToken cancellationToken
    ) {
        Result transactionResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                var merchant = await _merchantRepository.GetByIdAsync(message.MerchantId, ct);
                if (merchant is null) {
                    return Result.Failure(
                        DomainError.NotFound(
                            "Commerce.NotFound",
                            "El comercio indicado no existe."
                        )
                    );
                }

                if (
                    (message.IsActive && merchant.Status == MerchantStatus.Active)
                    || (!message.IsActive && merchant.Status == MerchantStatus.Inactive)
                ) {
                    Result unchanged = message.IsActive
                        ? merchant.Activate(_clock.Now)
                        : merchant.Deactivate(_clock.Now);
                    return Result.Failure(unchanged.Error!);
                }

                bool? associatedUserActive = null;
                if (!message.IsActive && merchant.AssociatedUserId is not null) {
                    associatedUserActive = await _userAccountService.GetActiveAsync(
                        merchant.AssociatedUserId,
                        ct
                    );
                    if (associatedUserActive is null) {
                        return Result.Failure(
                            DomainError.NotFound(
                                "User.NotFound",
                                "El usuario asociado al comercio no existe."
                            )
                        );
                    }
                }

                Result statusChange = message.IsActive
                    ? merchant.Activate(_clock.Now)
                    : merchant.Deactivate(_clock.Now);
                if (statusChange.IsFailure) {
                    return Result.Failure(statusChange.Error!);
                }

                if (!message.IsActive && merchant.AssociatedUserId is not null && associatedUserActive == true) {
                    Result userResult = await _userAccountService.SetActiveAsync(
                        merchant.AssociatedUserId,
                        false,
                        ct
                    );
                    if (userResult.IsFailure) {
                        return Result.Failure(userResult.Error!);
                    }
                }

                _merchantRepository.Update(merchant);
                return Result.Success();
            },
            ct: cancellationToken
        );

        return transactionResult.IsSuccess
            ? Result.Success(Unit.Value)
            : Result.Failure<Unit>(transactionResult.Error!);
    }
}
