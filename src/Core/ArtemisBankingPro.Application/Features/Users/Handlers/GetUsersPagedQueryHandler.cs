using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Users.Handlers;

/// <summary>
/// Lista usuarios de la aplicación web (excluye Comercio) paginados,
/// del más reciente al más antiguo, con filtro opcional por rol.
/// </summary>
public sealed class GetUsersPagedQueryHandler
    : IRequestHandler<GetUsersPagedQuery, Result<PageResult<UserListDto>>> {
    private readonly IUserRepository _userRepository;

    public GetUsersPagedQueryHandler(IUserRepository userRepository) {
        _userRepository = userRepository;
    }

    public async ValueTask<Result<PageResult<UserListDto>>> Handle(
        GetUsersPagedQuery message,
        CancellationToken cancellationToken
    ) {
        var page = new PageRequest(message.Page, message.PageSize);
        var result = await _userRepository.GetPagedAsync(
            message.Role,
            page,
            cancellationToken
        );

        return Result.Success(result);
    }
}

/// <summary>
/// Lista usuarios con rol Comercio paginados, del más reciente al más antiguo.
/// </summary>
public sealed class GetCommerceUsersPagedQueryHandler
    : IRequestHandler<GetCommerceUsersPagedQuery, Result<PageResult<UserListDto>>> {
    private readonly IUserRepository _userRepository;

    public GetCommerceUsersPagedQueryHandler(IUserRepository userRepository) {
        _userRepository = userRepository;
    }

    public async ValueTask<Result<PageResult<UserListDto>>> Handle(
        GetCommerceUsersPagedQuery message,
        CancellationToken cancellationToken
    ) {
        var page = new PageRequest(message.Page, message.PageSize);
        var result = await _userRepository.GetCommerceUsersPagedAsync(page, cancellationToken);

        return Result.Success(result);
    }
}
