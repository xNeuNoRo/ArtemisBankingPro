using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

/// <summary>
/// Consultas de lectura del módulo de cajero: indicadores diarios y operaciones
/// paginadas iniciadas por un cajero. Solo lectura; los montos y estados son
/// inmutables (historial financiero).
/// </summary>
public interface ICashierRepository {
    /// <summary>
    /// Indicadores diarios de un cajero para una fecha de negocio.
    /// </summary>
#pragma warning disable CA1716 // El nombre del parámetro lo exige el contrato del spec.
    Task<CashierDashboardDto> GetDashboardAsync(
        string cashierId,
        DateOnly date,
        CancellationToken ct = default
    );
#pragma warning restore CA1716

    /// <summary>
    /// Operaciones iniciadas por el cajero, paginadas y filtradas,
    /// las más recientes primero.
    /// </summary>
    Task<PageResult<CashierOperationDto>> GetOperationsPagedAsync(
        string cashierId,
        CashierOperationFilters filters,
        PageRequest page,
        CancellationToken ct = default
    );
}
