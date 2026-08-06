using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Lending.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Common;

public sealed class FinancialValueObjectTests {
    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("12345678A")]
    [InlineData(" 12345678")]
    public void AccountNumber_InvalidValue_ReturnsFailure(string value) {
        AccountNumber.Create(value).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void AccountAndLoanNumbers_PreserveLeadingZeros() {
        AccountNumber.Create("001234567").Value.Value.Should().Be("001234567");
        LoanNumber.Create("000000001").Value.Value.Should().Be("000000001");
    }

    [Fact]
    public void CardNumber_ToString_NeverRevealsPan() {
        CardNumber number = CardNumber.Create("4111111111111234").Value;

        number.ToString().Should().Be("************1234");
        number.ToString().Should().NotContain("411111111111");
    }

    [Fact]
    public void CvcDigest_ToString_NeverRevealsDigest() {
        CvcDigest digest = CvcDigest.Create(new string('a', 64)).Value;

        digest.ToString().Should().Be("[PROTECTED]");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("")]
    [InlineData("not-a-sha256-digest")]
    public void CvcDigest_InvalidRepresentation_ReturnsFailure(string value) {
        CvcDigest.Create(value).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void CardExpiration_ExpiresAfterLastDayOfMonth() {
        CardExpiration expiration = CardExpiration.Create(2, 2029).Value;

        expiration.IsExpired(new DateOnly(2029, 2, 28)).Should().BeFalse();
        expiration.IsExpired(new DateOnly(2029, 3, 1)).Should().BeTrue();
        expiration.Matches(2, 29).Should().BeTrue();
    }
}
