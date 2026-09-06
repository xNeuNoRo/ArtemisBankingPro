using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Admin.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Admin.Queries;

/// <summary>
/// Consulta los indicadores generales del sistema para el Home del
/// administrador (spec §16). Sin parámetros: todos los indicadores se
/// calculan con información de la base de datos y la fecha de negocio actual.
/// </summary>
public sealed record GetAdminDashboardQuery()
    : IRequest<Result<AdminDashboardDto>>, IAuthorize {
    public string[] RequiredRoles => ["Administrador"];
}
