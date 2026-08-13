using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Entities;
using Mediator;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<ChangeMerchantStatusCommandHandler> _logger;

    public ChangeMerchantStatusCommandHandler(
        IMerchantRepository merchantRepository,
        IUserAccountService userAccountService,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ILogger<ChangeMerchantStatusCommandHandler> logger
    ) {
        _merchantRepository = merchantRepository;
        _userAccountService = userAccountService;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<Result<Unit>> Handle(
        ChangeMerchantStatusCommand message,
        CancellationToken cancellationToken
    ) {
        // 1. El comercio debe existir.
        var merchant = await _merchantRepository.GetByIdAsync(
            message.MerchantId,
            cancellationToken
        );
        if (merchant is null) {
            return Result.Failure<Unit>(
                DomainError.NotFound(
                    "Commerce.NotFound",
                    "El comercio indicado no existe."
                )
            );
        }

        // 2. Al desactivar, el usuario asociado queda inactivo (spec §40).
        //    Al reactivar, los usuarios asociados no se activan automáticamente.
        //    La inactivación del usuario se resuelve antes de mutar el
        //    agregado: si falla, el comercio permanece sin cambios.
        string? associatedUserId = merchant.AssociatedUserId;
        bool userDeactivated = false;
        if (!message.IsActive && associatedUserId is not null) {
            var userResult = await _userAccountService.SetActiveAsync(
                associatedUserId,
                false,
                cancellationToken
            );
            if (userResult.IsFailure) {
                return Result.Failure<Unit>(userResult.Error!);
            }

            userDeactivated = true;
        }

        // 3. Cambiar el estado en el agregado (rechaza no-op y fechas inválidas).
        Result statusChange = message.IsActive
            ? merchant.Activate(_clock.Now)
            : merchant.Deactivate(_clock.Now);
        if (statusChange.IsFailure) {
            if (userDeactivated) {
                await CompensateUserReactivationAsync(
                    associatedUserId!,
                    merchant,
                    cancellationToken
                );
            }

            return Result.Failure<Unit>(statusChange.Error!);
        }

        // 4. Persistir el cambio de estado atómicamente. Si la persistencia
        //    falla tras haber inactivado el usuario, se compensa reactivándolo.
        var saveResult = await _unitOfWork.ExecuteInTransactionAsync(
            _ => {
                _merchantRepository.Update(merchant);
                return Task.FromResult(Result.Success());
            },
            ct: cancellationToken
        );

        if (saveResult.IsFailure) {
            if (userDeactivated) {
                await CompensateUserReactivationAsync(
                    associatedUserId!,
                    merchant,
                    cancellationToken
                );
            }

            return Result.Failure<Unit>(saveResult.Error!);
        }

        return Result.Success(Unit.Value);
    }

    private async Task CompensateUserReactivationAsync(
        string userId,
        Merchant merchant,
        CancellationToken cancellationToken
    ) {
        var compensation = await _userAccountService.SetActiveAsync(
            userId,
            true,
            cancellationToken
        );
        if (compensation.IsFailure) {
            _logger.LogWarning(
                "No se pudo reactivar el usuario {UserId} tras fallar la "
                    + "operación sobre el comercio {MerchantId}.",
                userId,
                merchant.Id
            );
        }
    }
}
