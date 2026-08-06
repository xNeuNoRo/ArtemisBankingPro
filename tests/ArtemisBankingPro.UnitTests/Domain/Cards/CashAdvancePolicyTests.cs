using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Policies;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Cards;

public sealed class CashAdvancePolicyTests {
    [Fact]
    public void Calculate_ValidPrincipal_AppliesSixPointTwentyFivePercentInterest() {
        CashAdvanceQuote quote = CashAdvancePolicy.Calculate(Money.Create(1_000m).Value).Value;

        quote.Principal.Amount.Should().Be(1_000m);
        quote.Interest.Amount.Should().Be(62.50m);
        quote.TotalCardCharge.Amount.Should().Be(1_062.50m);
    }

    [Fact]
    public void Calculate_FractionalInterest_RoundsAwayFromZero() {
        CashAdvanceQuote quote = CashAdvancePolicy.Calculate(Money.Create(0.24m).Value).Value;

        quote.Interest.Amount.Should().Be(0.02m);
    }
}
