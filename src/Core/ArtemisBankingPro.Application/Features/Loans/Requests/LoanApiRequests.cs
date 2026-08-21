namespace ArtemisBankingPro.Application.Features.Loans.Requests;

public sealed record CreateLoanRequest(
    string? ClientId,
    decimal? CapitalAmount,
    int? TermInMonths,
    decimal? AnnualInterestRate,
    bool? ConfirmHighRisk = false
);

public sealed record UpdateLoanRateRequest(decimal? AnnualInterestRate);
