using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Users.Handlers;

/// <summary>
/// Lista usuarios de la aplicación web (excluye Comercio) paginados,
/// del más reciente al más antiguo, con filtro opcional por rol.
/// </summary>
public sealed class GetUsersPagedQueryHandler
    : IRequestHandler<GetUsersPagedQuery, Result<PageResult<UserListResponse>>> {
    private readonly IUserRepository _userRepository;

    public GetUsersPagedQueryHandler(IUserRepository userRepository) {
        _userRepository = userRepository;
    }

    public async ValueTask<Result<PageResult<UserListResponse>>> Handle(
        GetUsersPagedQuery message,
        CancellationToken cancellationToken
    ) {
        var page = new PageRequest(message.Page, message.PageSize);
        string? canonicalRole = RoleSets.Mvc.FirstOrDefault(role =>
            string.Equals(role, message.Role, StringComparison.OrdinalIgnoreCase));
        var result = await _userRepository.GetPagedAsync(
            canonicalRole,
            page,
            cancellationToken
        );

        return Result.Success(
            new PageResult<UserListResponse>(
                result.Items.Select(ToResponse).ToArray(),
                result.TotalCount,
                result.Page,
                result.PageSize
            )
        );
    }

    private static UserListResponse ToResponse(UserListDto user) =>
        new(
            user.Id,
            user.UserName,
            user.Identification,
            user.FirstName,
            user.LastName,
            user.Email,
            user.Role,
            user.IsActive
        );
}

/// <summary>
/// Lista usuarios con rol Comercio paginados, del más reciente al más antiguo.
/// </summary>
public sealed class GetCommerceUsersPagedQueryHandler
    : IRequestHandler<GetCommerceUsersPagedQuery, Result<PageResult<CommerceUserListResponse>>> {
    private readonly IUserRepository _userRepository;
    private readonly IMerchantRepository _merchantRepository;

    public GetCommerceUsersPagedQueryHandler(
        IUserRepository userRepository,
        IMerchantRepository merchantRepository
    ) {
        _userRepository = userRepository;
        _merchantRepository = merchantRepository;
    }

    public async ValueTask<Result<PageResult<CommerceUserListResponse>>> Handle(
        GetCommerceUsersPagedQuery message,
        CancellationToken cancellationToken
    ) {
        var page = new PageRequest(message.Page, message.PageSize);
        var result = await _userRepository.GetCommerceUsersPagedAsync(page, cancellationToken);
        IReadOnlyList<MerchantUserAssociationDto> associations =
            await _merchantRepository.GetUserAssociationsAsync(
                result.Items.Select(user => user.Id).ToArray(),
                cancellationToken
            );
        Dictionary<string, MerchantUserAssociationDto> associationByUserId =
            associations.ToDictionary(item => item.UserId, StringComparer.Ordinal);

        if (result.Items.Any(user => !associationByUserId.ContainsKey(user.Id))) {
            throw new InvalidOperationException(
                "La consulta de usuarios Comercio encontró un usuario sin comercio asociado."
            );
        }

        return Result.Success(
            new PageResult<CommerceUserListResponse>(
                result.Items.Select(user => {
                    MerchantUserAssociationDto association = associationByUserId[user.Id];
                    return new CommerceUserListResponse(
                        user.Id,
                        user.UserName,
                        user.Identification,
                        user.FirstName,
                        user.LastName,
                        user.Email,
                        user.Role,
                        association.CommerceId,
                        association.CommerceName,
                        user.IsActive
                    );
                }).ToArray(),
                result.TotalCount,
                result.Page,
                result.PageSize
            )
        );
    }
}
