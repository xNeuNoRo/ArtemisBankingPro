namespace ArtemisBankingPro.Application.Features.Admin.DTOs;

/// <summary>
/// Indicadores generales del dashboard del administrador (spec §16-§18).
/// Los conteos de operaciones y pagos provienen del historial financiero; los
/// productos solo cuentan si están activos; la deuda promedio usa únicamente
/// clientes activos (RD$0.00 si no existen).
/// </summary>
public sealed record AdminDashboardDto(
    int TotalTransactionsHistorical,
    int TransactionsToday,
    int TotalPaymentsHistorical,
    int PaymentsToday,
    int ActiveClients,
    int InactiveClients,
    int TotalFinancialProducts,
    int ActiveLoans,
    int ActiveCreditCards,
    int ActiveSavingsAccounts,
    decimal AverageDebtPerClient
);
