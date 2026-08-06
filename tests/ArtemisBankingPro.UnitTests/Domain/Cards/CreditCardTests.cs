using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Cards;

public sealed class CreditCardTests {
    private static readonly DateOnly IssueDate = new(2026, 7, 29);
    private static readonly DateTimeOffset IssuedAt = new(2026, 7, 29, 12, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void Issue_ValidData_StartsActiveWithZeroDebt() {
        CreditCard card = CreateCard();

        card.Status.Should().Be(CreditCardStatus.Active);
        card.CurrentDebt.Should().Be(Money.Zero);
        card.AvailableCredit.Amount.Should().Be(10_000m);
        Assert.Equal(CardExpiration.Create(7, 2029).Value, card.Expiration);
    }

    [Fact]
    public void AuthorizeCharge_AmountAboveAvailableCredit_ReturnsFailureWithoutChangingDebt() {
        CreditCard card = CreateCard();

        Result result = card.AuthorizeCharge(Money.Create(10_000.01m).Value, IssueDate);

        Assert.Equal(CardErrors.InsufficientCredit, result.Error);
        card.CurrentDebt.Should().Be(Money.Zero);
    }

    [Fact]
    public void AuthorizeCharge_ExpiredCard_ReturnsFailure() {
        CreditCard card = CreateCard();

        Result result = card.AuthorizeCharge(Money.Create(1m).Value, new DateOnly(2029, 8, 1));

        Assert.Equal(CardErrors.Expired, result.Error);
    }

    [Fact]
    public void ApplyPayment_AmountAboveDebt_AppliesOnlyCurrentDebt() {
        CreditCard card = CreateCard();
        card.AuthorizeCharge(Money.Create(500m).Value, IssueDate);

        Money appliedAmount = card.ApplyPayment(Money.Create(1_000m).Value).Value;

        appliedAmount.Amount.Should().Be(500m);
        card.CurrentDebt.Should().Be(Money.Zero);
        card.AvailableCredit.Amount.Should().Be(10_000m);
    }

    [Fact]
    public void ApplyPayment_ExpiredActiveCard_IsAllowed() {
        CreditCard card = CreateCard();
        card.AuthorizeCharge(Money.Create(500m).Value, IssueDate);

        Result<Money> result = card.ApplyPayment(Money.Create(100m).Value);

        result.IsSuccess.Should().BeTrue();
        card.CurrentDebt.Amount.Should().Be(400m);
    }

    [Fact]
    public void ChangeCreditLimit_BelowDebt_ReturnsFailureWithoutChangingLimit() {
        CreditCard card = CreateCard();
        card.AuthorizeCharge(Money.Create(500m).Value, IssueDate);

        Result result = card.ChangeCreditLimit(Money.Create(499.99m).Value);

        Assert.Equal(CardErrors.LimitBelowDebt, result.Error);
        card.CreditLimit.Amount.Should().Be(10_000m);
    }

    [Fact]
    public void Cancel_WithDebt_ReturnsFailure() {
        CreditCard card = CreateCard();
        card.AuthorizeCharge(Money.Create(1m).Value, IssueDate);

        Assert.Equal(CardErrors.DebtMustBeZero, card.Cancel(IssuedAt.AddDays(1)).Error);
        card.Status.Should().Be(CreditCardStatus.Active);
    }

    private static CreditCard CreateCard() =>
        CreditCard.Issue(
            "customer",
            CardNumber.Create("4111111111111111").Value,
            CvcDigest.Create(new string('a', 64)).Value,
            Money.Create(10_000m).Value,
            "admin",
            IssuedAt,
            IssueDate).Value;
}
