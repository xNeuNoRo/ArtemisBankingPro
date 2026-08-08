using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Loans.Queries;

/// <summary>
/// Listado paginado de préstamos con filtro por estado y búsqueda por cédula
/// del cliente. Por defecto muestra los activos primero.
/// </summary>
public sealed record GetLoansPagedQuery(
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize,
    string? Status = null,
    string? Identification = null
) : IRequest<Result<PageResult<LoanListDto>>>, IAuthorize
{
    public string[] RequiredRoles => ["Administrador"];
}
