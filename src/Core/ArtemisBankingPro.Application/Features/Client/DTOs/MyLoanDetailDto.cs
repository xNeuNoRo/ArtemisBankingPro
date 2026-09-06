namespace ArtemisBankingPro.Application.Features.Client.DTOs;

public sealed record MyLoanDetailDto(
    int LoanId,
    string LoanNumber,
    decimal ApprovedPrincipal,
    decimal OutstandingAmount,
    decimal AnnualRate,
    int TermMonths,
    int TotalInstallments,
    int PaidInstallments,
    bool IsDelinquent,
    IReadOnlyList<AmortizationRowDto> Amortization
);

public sealed record AmortizationRowDto(
    int InstallmentNumber,
    DateOnly DueDate,
    decimal ScheduledAmount,
    decimal PaidAmount,
    string Status,
    bool IsOverdue
);
