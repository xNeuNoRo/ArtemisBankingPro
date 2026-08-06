using ArtemisBankingPro.Domain.Lending.Details;
using ArtemisBankingPro.Domain.Lending.Policies;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Lending;

public sealed class LoanRiskPolicyTests {
    [Fact]
    public void Evaluate_ProjectedDebtAboveAverage_ReturnsHighRisk() {
        LoanRiskAssessment assessment = LoanRiskPolicy.Evaluate(
            Money.Create(1_000m).Value,
            Money.Create(1_500m).Value,
            Money.Create(2_000m).Value);

        assessment.CurrentDebtExceedsAverage.Should().BeFalse();
        assessment.ProjectedDebtExceedsAverage.Should().BeTrue();
        assessment.IsHighRisk.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_DebtEqualToAverage_IsNotHighRisk() {
        LoanRiskAssessment assessment = LoanRiskPolicy.Evaluate(
            Money.Create(2_000m).Value,
            Money.Zero,
            Money.Create(2_000m).Value);

        assessment.IsHighRisk.Should().BeFalse();
    }
}
