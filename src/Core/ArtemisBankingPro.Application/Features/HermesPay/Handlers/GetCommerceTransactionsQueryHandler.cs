using ArtemisBankingPro.Application.Features.HermesPay.DTOs;
using ArtemisBankingPro.Application.Features.HermesPay.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Merchants.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.HermesPay.Handlers;

/// <summary>
/// Lista las transacciones recibidas por un comercio (spec §41,
/// GET /pay/get-transactions/{commerceId}). Con rol Comercio el comercio se
/// deriva del JWT y el commerceId recibido se ignora; con rol Administrador se
/// usa el commerceId del request. El comercio debe existir y estar activo.
/// </summary>
public sealed class GetCommerceTransactionsQueryHandler
    : IRequestHandler<GetCommerceTransactionsQuery, Result<GetCommerceTransactionsResponseDto>> {
    private readonly IMerchantRepository _merchantRepository;
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUserService _currentUser;

    public GetCommerceTransactionsQueryHandler(
        IMerchantRepository merchantRepository,
        ICreditCardRepository creditCardRepository,
        IUserRepository userRepository,
        ICurrentUserService currentUser
    ) {
        _merchantRepository = merchantRepository;
        _creditCardRepository = creditCardRepository;
        _userRepository = userRepository;
        _currentUser = currentUser;
    }

    public async ValueTask<Result<GetCommerceTransactionsResponseDto>> Handle(
        GetCommerceTransactionsQuery message,
        CancellationToken cancellationToken
    ) {
        // 1. Resolver el comercio según el rol del actor autenticado.
        bool isCommerceRole = _currentUser.Role == nameof(Roles.Comercio);
        int? resolvedCommerceId = isCommerceRole ? _currentUser.CommerceId : message.CommerceId;

        if (resolvedCommerceId is null or <= 0) {
            return Result.Failure<GetCommerceTransactionsResponseDto>(
                isCommerceRole
                    ? DomainError.Forbidden(
                        "Commerce.NotAssociated",
                        "El usuario de comercio no tiene un comercio asociado."
                    )
                    : DomainError.Validation(
                        "Commerce.IdRequired",
                        "Debe indicar el comercio a consultar."
                    )
            );
        }

        // 2. El comercio debe existir.
        var merchant = await _merchantRepository.GetByIdAsync(
            resolvedCommerceId.Value,
            cancellationToken
        );
        if (merchant is null) {
            return Result.Failure<GetCommerceTransactionsResponseDto>(
                DomainError.NotFound(
                    "Commerce.NotFound",
                    "El comercio indicado no existe."
                )
            );
        }

        // 3. Un comercio inactivo no puede consultar pagos (spec §41).
        if (merchant.Status != MerchantStatus.Active) {
            return Result.Failure<GetCommerceTransactionsResponseDto>(
                DomainError.Validation(
                    "Commerce.Inactive",
                    "El comercio está inactivo y no puede consultar pagos."
                )
            );
        }

        if (isCommerceRole) {
            if (merchant.AssociatedUserId != _currentUser.UserId) {
                return Result.Failure<GetCommerceTransactionsResponseDto>(
                    DomainError.Forbidden(
                        "Commerce.NotAssociated",
                        "El usuario de comercio no tiene un comercio asociado."
                    )
                );
            }

            UserListDto? commerceUser = await _userRepository.GetByIdAsync(
                _currentUser.UserId!,
                cancellationToken
            );
            if (commerceUser is null || !commerceUser.IsActive) {
                return Result.Failure<GetCommerceTransactionsResponseDto>(
                    DomainError.Forbidden(
                        "Auth.InactiveUser",
                        "El usuario de comercio está inactivo."
                    )
                );
            }
        }

        // 4. Consumos del comercio, paginados y más recientes primero.
        var page = new PageRequest(message.Page, message.PageSize);
        PageResult<CommerceTransactionDto> paged =
            await _creditCardRepository.GetConsumptionsByMerchantPagedAsync(
                resolvedCommerceId.Value,
                page,
                cancellationToken
            );

        return Result.Success(
            new GetCommerceTransactionsResponseDto(
                paged.Page,
                paged.PageSize,
                paged.TotalCount,
                paged.TotalPages,
                merchant.Id,
                merchant.Name,
                paged.Items
            )
        );
    }
}
