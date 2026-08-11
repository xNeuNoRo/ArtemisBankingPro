using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Queries;

/// <summary>
/// Indicadores diarios del cajero para una fecha de negocio: total de
/// transacciones, pagos realizados, depósitos y retiros.
/// </summary>
public sealed record GetCashierDashboardQuery(
    string CashierId,
    DateOnly Date
) : IRequest<Result<CashierDashboardDto>>, IAuthorize {
    public string[] RequiredRoles => ["Cajero"];
}
