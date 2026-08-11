using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Queries;

/// <summary>
/// Indicadores del Home del cajero para la fecha de negocio actual. El cajero
/// se obtiene del usuario autenticado y la fecha de IBusinessClock; la query
/// no recibe entrada del cliente.
/// </summary>
public sealed record GetCashierDashboardQuery : IRequest<Result<CashierDashboardDto>>, IAuthorize {
    public string[] RequiredRoles => ["Cajero", "Administrador"];
}
