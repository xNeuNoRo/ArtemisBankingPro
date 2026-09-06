using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Details;
using ArtemisBankingPro.Domain.Lending.Errors;
using ArtemisBankingPro.Domain.Lending.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.Policies;

/// <summary>
/// Calcula la amortización de un préstamo, generando una lista de cuotas con sus fechas, montos y desglose de interés/principal.
/// </summary>
public static class AmortizationCalculator {
    public static Result<IReadOnlyList<AmortizationEntry>> Generate(
        Money principal,
        InterestRate annualRate,
        int termMonths,
        DateOnly issuedDate
    ) {
        if (principal is null || annualRate is null) {
            return Result.Failure<IReadOnlyList<AmortizationEntry>>(LoanErrors.InvalidLoanData);
        }

        if (principal.Amount <= 0m) {
            return Result.Failure<IReadOnlyList<AmortizationEntry>>(
                LoanErrors.PrincipalMustBePositive
            );
        }

        if (!IsValidTerm(termMonths)) {
            return Result.Failure<IReadOnlyList<AmortizationEntry>>(LoanErrors.InvalidTerm);
        }

        if (Money.Round(principal.Amount / termMonths) == 0m) {
            return Result.Failure<IReadOnlyList<AmortizationEntry>>(
                LoanErrors.PrincipalTooSmallForTerm
            );
        }

        DateOnly[] dueDates = Enumerable
            .Range(1, termMonths)
            .Select(issuedDate.AddMonths)
            .ToArray();

        return TryCalculate(principal, annualRate, dueDates);
    }

    public static Result<IReadOnlyList<AmortizationEntry>> Recalculate(
        Money principal,
        InterestRate annualRate,
        IReadOnlyList<DateOnly> dueDates
    ) {
        if (principal is null || annualRate is null || dueDates is null) {
            return Result.Failure<IReadOnlyList<AmortizationEntry>>(LoanErrors.InvalidLoanData);
        }

        if (principal.Amount <= 0m) {
            return Result.Failure<IReadOnlyList<AmortizationEntry>>(
                LoanErrors.PrincipalMustBePositive
            );
        }

        if (dueDates.Count == 0) {
            return Result.Failure<IReadOnlyList<AmortizationEntry>>(
                LoanErrors.NoEligibleInstallments
            );
        }

        if (Money.Round(principal.Amount / dueDates.Count) == 0m) {
            return Result.Failure<IReadOnlyList<AmortizationEntry>>(
                LoanErrors.PrincipalTooSmallForTerm
            );
        }

        return TryCalculate(principal, annualRate, dueDates);
    }

    public static bool IsValidTerm(int termMonths) =>
        termMonths is >= 6 and <= 60 && termMonths % 6 == 0;

    private static Result<IReadOnlyList<AmortizationEntry>> TryCalculate(
        Money principal,
        InterestRate annualRate,
        IReadOnlyList<DateOnly> dueDates
    ) {
        try {
            return Result.Success<IReadOnlyList<AmortizationEntry>>(
                Calculate(principal, annualRate, dueDates)
            );
        }
        catch (ArithmeticException) {
            return Result.Failure<IReadOnlyList<AmortizationEntry>>(
                LoanErrors.UnsupportedInterestRate
            );
        }
    }

    private static List<AmortizationEntry> Calculate(
        Money principal,
        InterestRate annualRate,
        IReadOnlyList<DateOnly> dueDates
    ) {
        decimal monthlyRate = annualRate.MonthlyRate;
        decimal fixedPayment = CalculateFixedPayment(principal.Amount, monthlyRate, dueDates.Count);
        decimal remainingPrincipal = principal.Amount;
        List<AmortizationEntry> entries = new(dueDates.Count);

        for (int index = 0; index < dueDates.Count; index++) {
            bool isLast = index == dueDates.Count - 1;
            decimal interest = Money.Round(remainingPrincipal * monthlyRate);
            decimal principalAmount = isLast
                ? remainingPrincipal
                : Money.Round(fixedPayment - interest);

            if (principalAmount > remainingPrincipal) {
                principalAmount = remainingPrincipal;
            }

            decimal scheduledAmount = Money.Round(principalAmount + interest);
            entries.Add(
                new AmortizationEntry(
                    index + 1,
                    dueDates[index],
                    Money.FromDecimal(scheduledAmount),
                    Money.FromDecimal(interest),
                    Money.FromDecimal(principalAmount)
                )
            );

            remainingPrincipal = Money.Round(remainingPrincipal - principalAmount);
        }

        return entries;
    }

    private static decimal CalculateFixedPayment(decimal principal, decimal monthlyRate, int months) {
        if (monthlyRate == 0m) {
            return Money.Round(principal / months);
        }

        decimal factor = Pow(1m + monthlyRate, months);
        return Money.Round(principal * (monthlyRate * factor) / (factor - 1m));
    }

    private static decimal Pow(decimal value, int exponent) {
        decimal result = 1m;
        for (int index = 0; index < exponent; index++) {
            result *= value;
        }

        return result;
    }
}
