namespace ArtemisBankingPro.Application.Features.Cashier.DTOs;

public class CashierDashboardDto {
    public int TotalTransactions { get; set; }
    public decimal PaymentsToday { get; set; }
    public int DepositsToday { get; set; }
    public int WithdrawalsToday { get; set; }
}
