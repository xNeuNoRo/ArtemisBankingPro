using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Users.Queries;

/// <summary>
/// Detalle de un usuario con su cuenta de ahorro principal (si existe).
/// </summary>
public sealed record GetUserByIdQuery(string UserId)
    : IRequest<Result<UserDetailResponse>>, IAuthorize
{
    public string[] RequiredRoles => ["Administrador"];
}
