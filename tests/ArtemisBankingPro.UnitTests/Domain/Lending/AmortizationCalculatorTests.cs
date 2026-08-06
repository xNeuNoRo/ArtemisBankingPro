using ArtemisBankingPro.Domain.Lending.Details;
using ArtemisBankingPro.Domain.Lending.Errors;
using ArtemisBankingPro.Domain.Lending.Policies;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Lending;

public sealed class AmortizationCalculatorTests {
    [Fact]
    public void Generate_ZeroRate_DividesPrincipalEvenly() {
        IReadOnlyList<AmortizationEntry> schedule = AmortizationCalculator.Generate(
            Money.Create(1_200m).Value,
            InterestRate.Create(0m).Value,
            6,
            new DateOnly(2026, 1, 15)).Value;

        schedule.Should().HaveCount(6);
        schedule.Should().OnlyContain(entry => entry.ScheduledAmount.Amount == 200m);
        schedule.Sum(entry => entry.PrincipalAmount.Amount).Should().Be(1_200m);
    }

    [Fact]
    public void Generate_StandardLoan_UsesFrenchPaymentAndClearsPrincipal() {
        IReadOnlyList<AmortizationEntry> schedule = AmortizationCalculator.Generate(
            Money.Create(100_000m).Value,
            InterestRate.Create(12m).Value,
            12,
            new DateOnly(2026, 1, 15)).Value;

        schedule[0].ScheduledAmount.Amount.Should().Be(8_884.88m);
        schedule.Take(11).Select(entry => entry.ScheduledAmount.Amount).Should().OnlyContain(amount => amount == 8_884.88m);
        schedule.Sum(entry => entry.PrincipalAmount.Amount).Should().Be(100_000m);
    }

    [Fact]
    public void Generate_Day31_PreservesOriginalAnchorAcrossMonths() {
        IReadOnlyList<AmortizationEntry> schedule = AmortizationCalculator.Generate(
            Money.Create(600m).Value,
            InterestRate.Create(0m).Value,
            6,
            new DateOnly(2027, 1, 31)).Value;

        schedule.Select(entry => entry.DueDate).Should().ContainInOrder(
            new DateOnly(2027, 2, 28),
            new DateOnly(2027, 3, 31),
            new DateOnly(2027, 4, 30));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(61)]
    public void Generate_InvalidTerm_ReturnsFailure(int term) {
        Result<IReadOnlyList<AmortizationEntry>> result = AmortizationCalculator.Generate(
                Money.Create(1_000m).Value,
                InterestRate.Create(1m).Value,
                term,
                new DateOnly(2026, 1, 1));

        Assert.Equal(LoanErrors.InvalidTerm, result.Error);
    }

    [Fact]
    public void Generate_PrincipalTooSmallForTerm_ReturnsFailureInsteadOfZeroInstallments() {
        Result<IReadOnlyList<AmortizationEntry>> result = AmortizationCalculator.Generate(
            Money.Create(0.01m).Value,
            InterestRate.Create(0m).Value,
            60,
            new DateOnly(2026, 1, 1));

        Assert.Equal(LoanErrors.PrincipalTooSmallForTerm, result.Error);
    }
}
