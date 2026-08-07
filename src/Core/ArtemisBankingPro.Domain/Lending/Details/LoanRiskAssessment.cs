using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.Details;

public sealed record LoanRiskAssessment(Money CurrentDebt, Money ProjectedDebt, Money AverageDebt) {
    public bool CurrentDebtExceedsAverage => CurrentDebt > AverageDebt;

    public bool ProjectedDebtExceedsAverage => ProjectedDebt > AverageDebt;

    public bool IsHighRisk => CurrentDebtExceedsAverage || ProjectedDebtExceedsAverage;
}
