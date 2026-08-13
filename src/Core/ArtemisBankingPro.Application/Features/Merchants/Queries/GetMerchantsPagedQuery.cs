using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Merchants.Queries;

/// <summary>
/// Listado paginado de comercios (spec §40, GET /api/commerce). El parámetro
/// status admite activo, inactivo o todos; si no se envía, se devuelven solo
/// los comercios activos. Ordenados del más reciente al más antiguo.
/// </summary>
public sealed record GetMerchantsPagedQuery(
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize,
    string? Status = null
) : IRequest<Result<GetMerchantsPagedResponseDto>>, IAuthorize {
    public string[] RequiredRoles => ["Administrador"];
}
