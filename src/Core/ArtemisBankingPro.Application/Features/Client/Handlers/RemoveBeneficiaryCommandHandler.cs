using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

/// <summary>
/// Elimina únicamente la relación de beneficiario perteneciente al cliente
/// autenticado, sin modificar la cuenta destino ni su historial financiero.
/// </summary>
public sealed class RemoveBeneficiaryCommandHandler
    : IRequestHandler<RemoveBeneficiaryCommand, Result<Unit>> {
    private readonly IBeneficiaryRepository _beneficiaryRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public RemoveBeneficiaryCommandHandler(
        IBeneficiaryRepository beneficiaryRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser
    ) {
        _beneficiaryRepository = beneficiaryRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async ValueTask<Result<Unit>> Handle(
        RemoveBeneficiaryCommand message,
        CancellationToken cancellationToken
    ) {
        Result result = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                Beneficiary? beneficiary = await _beneficiaryRepository.GetByIdAsync(
                    message.BeneficiaryId,
                    ct
                );
                if (beneficiary is null) {
                    return Result.Failure(
                        DomainError.NotFound(
                            "Beneficiary.NotFound",
                            "El beneficiario indicado no existe."
                        )
                    );
                }

                if (beneficiary.OwnerUserId != _currentUser.UserId) {
                    throw new ForbiddenAccessException(
                        "El beneficiario no pertenece al cliente autenticado."
                    );
                }

                _beneficiaryRepository.Delete(beneficiary);
                return Result.Success();
            },
            ct: cancellationToken
        );

        return result.IsSuccess
            ? Result.Success(Unit.Value)
            : Result.Failure<Unit>(result.Error!);
    }
}
