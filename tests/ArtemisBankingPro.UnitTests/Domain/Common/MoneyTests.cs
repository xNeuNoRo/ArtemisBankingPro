using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Common;

public sealed class MoneyTests {
    [Theory]
    [InlineData(1.005, 1.01)]
    [InlineData(1.004, 1.00)]
    [InlineData(0, 0)]
    public void Create_RoundsToTwoDecimalsAwayFromZero(decimal input, decimal expected) {
        Money.Create(input).Value.Amount.Should().Be(expected);
    }

    [Fact]
    public void Create_NegativeAmount_ReturnsFailure() {
        Result<Money> result = Money.Create(-0.01m);

        result.IsFailure.Should().BeTrue();
        Assert.Equal(DomainError.Validation("Money.Negative", "El monto no puede ser negativo."), result.Error);
    }

    [Fact]
    public void Subtract_AmountAboveBalance_ReturnsFailureWithoutChangingOriginal() {
        Money original = Money.Create(10m).Value;

        Result<Money> result = original.Subtract(Money.Create(10.01m).Value);

        result.IsFailure.Should().BeTrue();
        original.Amount.Should().Be(10m);
    }
}
