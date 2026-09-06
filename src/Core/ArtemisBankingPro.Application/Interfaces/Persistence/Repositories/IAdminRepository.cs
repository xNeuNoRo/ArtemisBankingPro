using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

/// <summary>
/// Conteos de operaciones financieras para el dashboard administrativo
/// (spec §16): totales históricos y del día de negocio, y pagos aprobados
/// (tarjeta de crédito y préstamo) históricos y del día.
/// </summary>
public sealed record OperationDashboardCounts(
    int TotalTransactions,
    int TodayTransactions,
    int TotalPayments,
    int TodayPayments
);

public sealed record ClientAssignmentFinancialFacts(
    bool HasActiveLoan,
    bool HasPrincipalSavingsAccount,
    decimal TotalDebt
);

/// <summary>
/// Repositorio de lectura con agregados del dashboard del administrador
/// (spec §16-§18): conteos de operaciones/pagos, productos activos y deuda
/// de clientes activos. Los agregados se calculan en SQL Server.
/// </summary>
public interface IAdminRepository {
    /// <summary>
    /// Conteos de <see cref="FinancialOperation"/>: todas las registradas
    /// (aprobadas y rechazadas) y los pagos aprobados a tarjeta/préstamo,
    /// tanto históricos como del día de negocio indicado.
    /// </summary>
    Task<OperationDashboardCounts> GetOperationCountsAsync(
        DateOnly businessDate,
        CancellationToken ct = default
    );

    /// <summary>Cantidad de préstamos en estado activo.</summary>
    Task<int> CountActiveLoansAsync(CancellationToken ct = default);

    /// <summary>Cantidad de tarjetas de crédito en estado activo.</summary>
    Task<int> CountActiveCreditCardsAsync(CancellationToken ct = default);

    /// <summary>
    /// Cantidad de cuentas de ahorro activas (principales y secundarias).
    /// </summary>
    Task<int> CountActiveSavingsAccountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Suma de la deuda (pendiente de préstamos activos + deuda de tarjetas
    /// activas) de los clientes cuyos identificadores se reciben; los dueños
    /// inactivos no aportan deuda al promedio (spec §559-569).
    /// </summary>
    Task<Money> GetActiveClientDebtAsync(
        IReadOnlyCollection<string> activeClientIds,
        CancellationToken ct = default
    );

    /// <summary>
    /// Obtiene hechos financieros agrupados para un conjunto de clientes. La
    /// consulta no depende del número de clientes y no incluye datos de Identity.
    /// </summary>
    Task<IReadOnlyDictionary<string, ClientAssignmentFinancialFacts>>
        GetClientAssignmentFactsAsync(
        IReadOnlyCollection<string> clientIds,
        CancellationToken ct = default
    );
}
