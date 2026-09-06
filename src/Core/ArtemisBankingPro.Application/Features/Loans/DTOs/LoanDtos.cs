namespace ArtemisBankingPro.Application.Features.Loans.DTOs;

/// <summary>Elemento del listado de préstamos.</summary>
public sealed record LoanListDto(
    int LoanId,
    string LoanNumber,
    string CustomerUserId,
    string CustomerFullName,
    decimal CapitalAmount,
    int TotalInstallments,
    int PaidInstallments,
    decimal PendingAmount,
    decimal AnnualInterestRate,
    int TermMonths,
    string Status,
    string CustomerPaymentStatus,
    DateTimeOffset IssuedAt
);

/// <summary>Cuota de la tabla de amortización.</summary>
public sealed record AmortizationEntryDto(
    int InstallmentNumber,
    DateOnly DueDate,
    decimal InstallmentAmount,
    decimal InterestAmount,
    decimal CapitalAmount,
    decimal PendingInstallmentAmount,
    string PaymentStatus,
    bool IsLate
);

/// <summary>Detalle de préstamo con su tabla de amortización.</summary>
public sealed record LoanDetailDto(
    int LoanId,
    string LoanNumber,
    string CustomerUserId,
    string CustomerFullName,
    decimal CapitalAmount,
    decimal AnnualInterestRate,
    int TermMonths,
    decimal MonthlyInstallment,
    decimal PendingAmount,
    string Status,
    string CustomerPaymentStatus,
    DateTimeOffset IssuedAt,
    IReadOnlyList<AmortizationEntryDto> Amortization
);
