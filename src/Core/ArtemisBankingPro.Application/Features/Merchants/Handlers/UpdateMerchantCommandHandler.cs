using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Merchants.Handlers;

/// <summary>
/// Actualiza la información de un comercio sin modificar su estado
/// (spec §40, PUT /api/commerce/{id}). El RNC y el correo no pueden
/// pertenecer a otro comercio.
/// </summary>
public sealed class UpdateMerchantCommandHandler
    : IRequestHandler<UpdateMerchantCommand, Result<Unit>> {
    private readonly IMerchantRepository _merchantRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;

    public UpdateMerchantCommandHandler(
        IMerchantRepository merchantRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock
    ) {
        _merchantRepository = merchantRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async ValueTask<Result<Unit>> Handle(
        UpdateMerchantCommand message,
        CancellationToken cancellationToken
    ) {
        // El comercio debe existir.
        var merchant = await _merchantRepository.GetByIdAsync(
            message.MerchantId,
            cancellationToken
        );
        if (merchant is null) {
            return Result.Failure<Unit>(
                DomainError.NotFound("Commerce.NotFound", "El comercio indicado no existe.")
            );
        }

        // El RNC y el correo no pueden pertenecer a otro comercio.
        string trimmedRnc = message.Rnc.Trim();
        if (
            await _merchantRepository.GetByRncAsync(trimmedRnc, cancellationToken) is { } rncOwner
            && rncOwner.Id != merchant.Id
        ) {
            return Result.Failure<Unit>(
                DomainError.Conflict("Commerce.RncExists", "El RNC pertenece a otro comercio.")
            );
        }

        string normalizedEmail = message.Email.Trim().ToLowerInvariant();
        if (
            await _merchantRepository.ExistsAsync(
                candidate => candidate.Email == normalizedEmail && candidate.Id != merchant.Id,
                cancellationToken
            )
        ) {
            return Result.Failure<Unit>(
                DomainError.Conflict(
                    "Commerce.EmailExists",
                    "El correo electrónico pertenece a otro comercio."
                )
            );
        }

        // Actualizamos el agregado y persistimos atómicamente.
        var updateResult = merchant.UpdateInformation(
            message.Name,
            message.Description,
            message.Email,
            message.PhoneNumber,
            message.Rnc,
            _clock.Now
        );
        if (updateResult.IsFailure) {
            return Result.Failure<Unit>(updateResult.Error!);
        }

        var saveResult = await _unitOfWork.ExecuteInTransactionAsync(
            _ => {
                _merchantRepository.Update(merchant);
                return Task.FromResult(Result.Success());
            },
            ct: cancellationToken
        );

        return saveResult.IsSuccess
            ? Result.Success(Unit.Value)
            : Result.Failure<Unit>(saveResult.Error!);
    }
}
