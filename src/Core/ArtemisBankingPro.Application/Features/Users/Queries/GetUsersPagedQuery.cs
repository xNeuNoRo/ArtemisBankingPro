using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Users.Queries;

/// <summary>
/// Listado paginado de usuarios de la aplicación web (excluye rol Comercio),
/// opcionalmente filtrado por rol, del más reciente al más antiguo.
/// </summary>
public sealed record GetUsersPagedQuery(
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize,
    string? Role = null
) : IRequest<Result<PageResult<UserListResponse>>>, IAuthorize {
    public string[] RequiredRoles => ["Administrador"];
}
