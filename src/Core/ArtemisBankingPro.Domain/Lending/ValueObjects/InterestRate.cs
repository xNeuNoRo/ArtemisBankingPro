using ArtemisBankingPro.Domain.Lending.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.ValueObjects;

public sealed record InterestRate {
    private InterestRate(decimal annualPercentage) {
        AnnualPercentage = annualPercentage;
    }

    public decimal AnnualPercentage { get; }

    public decimal MonthlyRate => AnnualPercentage / 100m / 12m;

    public static Result<InterestRate> Create(decimal annualPercentage) {
        if (annualPercentage < 0m) {
            return Result.Failure<InterestRate>(LoanErrors.NegativeInterestRate);
        }

        return Result.Success(new InterestRate(annualPercentage));
    }

    public override string ToString() => $"{AnnualPercentage}%";
}
