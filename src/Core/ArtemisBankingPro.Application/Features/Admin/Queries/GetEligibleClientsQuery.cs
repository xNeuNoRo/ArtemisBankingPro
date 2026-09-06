using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Admin.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Admin.Queries;

/// <summary>
/// Lista clientes activos que cumplen las precondiciones de selección de un
/// producto administrativo. La deuda total y el promedio se calculan siempre
/// en servidor usando el estado financiero actual.
/// </summary>
public sealed record GetEligibleClientsQuery(
    ClientAssignmentProduct Product,
    int Page = PageRequest.DefaultPage,
    int PageSize = PageRequest.DefaultPageSize,
    string? Identification = null
) : IRequest<Result<EligibleClientsResponse>>, IAuthorize {
    public string[] RequiredRoles => ["Administrador"];
}
