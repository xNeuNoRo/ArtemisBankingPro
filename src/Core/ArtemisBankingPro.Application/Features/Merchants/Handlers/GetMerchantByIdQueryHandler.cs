using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Application.Features.Merchants.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Merchants.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Merchants.Handlers;

/// <summary>
/// Devuelve el detalle de un comercio (spec §40, GET /api/commerce/{id})
/// incluyendo el usuario asociado, si existe.
/// </summary>
public sealed class GetMerchantByIdQueryHandler
    : IRequestHandler<GetMerchantByIdQuery, Result<MerchantDetailDto>> {
    private readonly IMerchantRepository _merchantRepository;
    private readonly IUserRepository _userRepository;

    public GetMerchantByIdQueryHandler(
        IMerchantRepository merchantRepository,
        IUserRepository userRepository
    ) {
        _merchantRepository = merchantRepository;
        _userRepository = userRepository;
    }

    public async ValueTask<Result<MerchantDetailDto>> Handle(
        GetMerchantByIdQuery message,
        CancellationToken cancellationToken
    ) {
        var merchant = await _merchantRepository.GetByIdAsync(
            message.MerchantId,
            cancellationToken
        );
        if (merchant is null) {
            return Result.Failure<MerchantDetailDto>(
                DomainError.NotFound(
                    "Commerce.NotFound",
                    "El comercio indicado no existe."
                )
            );
        }

        MerchantUserDto? associatedUser = null;
        if (merchant.AssociatedUserId is not null) {
            var user = await _userRepository.GetByIdAsync(
                merchant.AssociatedUserId,
                cancellationToken
            );
            if (user is not null) {
                associatedUser = new MerchantUserDto(
                    user.Id,
                    user.UserName,
                    user.Email,
                    user.IsActive
                );
            }
        }

        return Result.Success(
            new MerchantDetailDto(
                merchant.Id,
                merchant.Name,
                merchant.Description,
                merchant.Email,
                merchant.PhoneNumber,
                merchant.Rnc,
                merchant.Status == MerchantStatus.Active,
                merchant.CreatedAt,
                associatedUser
            )
        );
    }
}
