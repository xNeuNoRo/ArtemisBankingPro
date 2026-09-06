using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Merchants.Handlers;

/// <summary>
/// Crea un comercio en estado Activo (spec §40, POST /api/commerce). La
/// unicidad de RNC y correo se valida aquí y se refuerza con índices únicos
/// en la base de datos.
/// </summary>
public sealed class CreateMerchantCommandHandler
    : IRequestHandler<CreateMerchantCommand, Result<CreateMerchantResponse>> {
    private readonly IMerchantRepository _merchantRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IBusinessClock _clock;

    public CreateMerchantCommandHandler(
        IMerchantRepository merchantRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IBusinessClock clock
    ) {
        _merchantRepository = merchantRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async ValueTask<Result<CreateMerchantResponse>> Handle(
        CreateMerchantCommand message,
        CancellationToken cancellationToken
    ) {
        // Unicidad de RNC y correo (validada aquí y reforzada por índices
        //    únicos en SQL Server).
        if (await _merchantRepository.ExistsByRncAsync(message.Rnc.Trim(), cancellationToken)) {
            return Result.Failure<CreateMerchantResponse>(
                DomainError.Conflict(
                    "Commerce.RncExists",
                    "Ya existe un comercio con el mismo RNC."
                )
            );
        }

        string normalizedEmail = message.Email.Trim().ToLowerInvariant();
        if (await _merchantRepository.ExistsByEmailAsync(normalizedEmail, cancellationToken)) {
            return Result.Failure<CreateMerchantResponse>(
                DomainError.Conflict(
                    "Commerce.EmailExists",
                    "Ya existe un comercio con el mismo correo electrónico."
                )
            );
        }

        // Reglas de dominio (campos obligatorios y formato de correo).
        var createResult = Merchant.Create(
            message.Name,
            message.Description,
            message.Email,
            message.PhoneNumber,
            message.Rnc,
            _currentUser.UserId!,
            _clock.Now
        );
        if (createResult.IsFailure) {
            return Result.Failure<CreateMerchantResponse>(createResult.Error!);
        }

        var merchant = createResult.Value;

        // Persistimos atómicamente (un único insert con índice único).
        var saveResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                await _merchantRepository.AddAsync(merchant, ct);
                return Result.Success();
            },
            ct: cancellationToken
        );
        if (saveResult.IsFailure) {
            return Result.Failure<CreateMerchantResponse>(saveResult.Error!);
        }

        return Result.Success(
            new CreateMerchantResponse(
                merchant.Id,
                merchant.Name,
                merchant.Description,
                merchant.Email,
                merchant.PhoneNumber,
                merchant.Rnc,
                merchant.Status == MerchantStatus.Active,
                merchant.CreatedAt
            )
        );
    }
}
