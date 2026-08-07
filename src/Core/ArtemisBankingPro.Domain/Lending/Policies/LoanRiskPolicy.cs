using ArtemisBankingPro.Domain.Lending.Details;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.Policies;

public static class LoanRiskPolicy {
    public static LoanRiskAssessment Evaluate(
        Money currentDebt,
        Money newLoanTotalPayable,
        Money averageDebt
    ) {
        Money projectedDebt = currentDebt.Add(newLoanTotalPayable);
        return new LoanRiskAssessment(currentDebt, projectedDebt, averageDebt);
    }
}
