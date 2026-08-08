namespace ArtemisBankingPro.Application.Features.Loans.DTOs;

/// <summary>Respuesta de asignación de préstamo.</summary>
public sealed record CreateLoanResponse(
    int LoanId,
    string LoanNumber,
    string CustomerUserId,
    string CustomerFullName,
    decimal CapitalAmount,
    int TermMonths,
    decimal AnnualInterestRate,
    decimal MonthlyInstallment,
    decimal TotalAmountToPay,
    string Status,
    DateTimeOffset IssuedAt
);
