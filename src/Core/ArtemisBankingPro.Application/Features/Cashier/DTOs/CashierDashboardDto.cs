namespace ArtemisBankingPro.Application.Features.Cashier.DTOs;

/// <summary>
/// Indicadores diarios del cajero autenticado para la fecha de negocio actual.
/// TransactionsToday cuenta todas las operaciones del día; PaymentsToday solo
/// los pagos a tarjeta y a préstamo aprobados.
/// </summary>
public sealed record CashierDashboardDto(
    int TransactionsToday,
    int PaymentsToday,
    int DepositsToday,
    int WithdrawalsToday
);
