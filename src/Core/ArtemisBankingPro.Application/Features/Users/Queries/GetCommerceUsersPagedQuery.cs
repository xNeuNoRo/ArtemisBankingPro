using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Users.Queries;

/// <summary>
/// Listado paginado de usuarios con rol Comercio, del más reciente al más antiguo.
/// </summary>
public sealed record GetCommerceUsersPagedQuery(
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize
) : IRequest<Result<PageResult<CommerceUserListResponse>>>, IAuthorize {
    public string[] RequiredRoles => ["Administrador"];
}
