using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Application.Features.Merchants.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using MapsterMapper;
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
    private readonly IMapper _mapper;

    public GetMerchantByIdQueryHandler(
        IMerchantRepository merchantRepository,
        IUserRepository userRepository,
        IMapper mapper
    ) {
        _merchantRepository = merchantRepository;
        _userRepository = userRepository;
        _mapper = mapper;
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
            if (user is not null && user.Role == nameof(Roles.Comercio)) {
                associatedUser = new MerchantUserDto(
                    user.Id,
                    user.UserName,
                    user.Email,
                    user.IsActive
                );
            }
        }

        // Mapeo Entities → DTO con Mapster (requerimiento del documento
        // funcional, ADR-011); el usuario asociado se compone aparte por ser
        // una consulta adicional.
        return Result.Success(
            _mapper.Map<MerchantDetailDto>(merchant) with { AssociatedUser = associatedUser }
        );
    }
}
