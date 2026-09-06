using System.Text.Json.Serialization;

namespace ArtemisBankingPro.Application.Features.Loans.DTOs;

public sealed record LoanApiListDto(
    string Id,
    string LoanNumber,
    string ClientId,
    string ClientFullName,
    decimal CapitalAmount,
    int TotalInstallments,
    int PaidInstallments,
    decimal PendingAmount,
    decimal AnnualInterestRate,
    int TermInMonths,
    string Status,
    string ClientPaymentStatus,
    DateTimeOffset CreatedAt
);

public sealed record LoanApiAmortizationEntryDto(
    int InstallmentNumber,
    DateOnly DueDate,
    decimal InstallmentAmount,
    decimal InterestAmount,
    decimal CapitalAmount,
    decimal PendingInstallmentAmount,
    string PaymentStatus,
    bool IsLate
);

public sealed record LoanApiDetailDto(
    string Id,
    string LoanNumber,
    string ClientId,
    string ClientFullName,
    decimal CapitalAmount,
    decimal AnnualInterestRate,
    int TermInMonths,
    decimal MonthlyInstallment,
    decimal PendingAmount,
    string Status,
    string ClientPaymentStatus,
    DateTimeOffset CreatedAt,
    IReadOnlyList<LoanApiAmortizationEntryDto> Amortization
);

public sealed record CreateLoanApiResponse(
    string Id,
    string LoanNumber,
    string ClientId,
    string ClientFullName,
    decimal CapitalAmount,
    int TermInMonths,
    decimal AnnualInterestRate,
    decimal MonthlyInstallment,
    decimal TotalAmountToPay,
    string Status,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? NotificationWarning = null
);
