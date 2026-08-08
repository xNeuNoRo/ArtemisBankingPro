using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Cards.Events;
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

    [Fact]
    public void Issue_RaisesCardAssignedEvent_WithLastFourLimitAndExpiration() {
        CreditCard card = CreateCard();

        card.DomainEvents.Should().ContainSingle();
        CardAssignedEvent? domainEvent = Assert.IsType<CardAssignedEvent>(card.DomainEvents.Single());

        domainEvent.CustomerUserId.Should().Be("customer");
        domainEvent.LastFour.Should().Be("1111");
        domainEvent.CreditLimit.Should().Be(10_000m);
        domainEvent.ExpirationMonth.Should().Be(7);
        domainEvent.ExpirationYear.Should().Be(2029);
    }

    [Fact]
    public void Issue_InvalidLimit_DoesNotRaiseEvent() {
        Result<CreditCard> result = CreditCard.Issue(
            "customer",
            "1111",
            new string('a', 64),
            CvcDigest.Create(new string('b', 64)).Value,
            Money.Create(0m).Value,
            "admin",
            IssuedAt,
            IssueDate
        );

        result.IsFailure.Should().BeTrue();
        Assert.Equal(CardErrors.LimitMustBePositive, result.Error);
    }

    [Fact]
    public void ChangeCreditLimit_RaisesLimitChangedEvent() {
        CreditCard card = CreateCard();

        card.ChangeCreditLimit(Money.Create(15_000m).Value).IsSuccess.Should().BeTrue();

        CardLimitChangedEvent? domainEvent = Assert.IsType<CardLimitChangedEvent>(
            card.DomainEvents.OfType<CardLimitChangedEvent>().Single()
        );
        domainEvent.CustomerUserId.Should().Be("customer");
        domainEvent.LastFour.Should().Be("1111");
        domainEvent.NewCreditLimit.Should().Be(15_000m);
    }

    [Fact]
    public void ChangeCreditLimit_BelowDebt_DoesNotRaiseEvent() {
        CreditCard card = CreateCard();
        card.AuthorizeCharge(Money.Create(500m).Value, IssueDate);

        card.ChangeCreditLimit(Money.Create(499.99m).Value).IsFailure.Should().BeTrue();

        card.DomainEvents.OfType<CardLimitChangedEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Issue_MissingCustomerOrAssigner_ReturnsFailure() {
        Result<CreditCard> missingCustomer = CreditCard.Issue(
            " ",
            "1111",
            new string('a', 64),
            CvcDigest.Create(new string('b', 64)).Value,
            Money.Create(10_000m).Value,
            "admin",
            IssuedAt,
            IssueDate);

        Result<CreditCard> missingAssigner = CreditCard.Issue(
            "customer",
            "1111",
            new string('a', 64),
            CvcDigest.Create(new string('b', 64)).Value,
            Money.Create(10_000m).Value,
            " ",
            IssuedAt,
            IssueDate);

        Assert.Equal(CardErrors.InvalidCustomer, missingCustomer.Error);
        Assert.Equal(CardErrors.InvalidAssigner, missingAssigner.Error);
    }

    [Fact]
    public void Issue_InvalidLastFourOrFingerprint_ReturnsFailure() {
        Result<CreditCard> invalidLastFour = CreditCard.Issue(
            "customer",
            "12",
            new string('a', 64),
            CvcDigest.Create(new string('b', 64)).Value,
            Money.Create(10_000m).Value,
            "admin",
            IssuedAt,
            IssueDate);

        Result<CreditCard> invalidFingerprint = CreditCard.Issue(
            "customer",
            "1111",
            "short",
            CvcDigest.Create(new string('b', 64)).Value,
            Money.Create(10_000m).Value,
            "admin",
            IssuedAt,
            IssueDate);

        Assert.Equal(CardErrors.InvalidLastFour, invalidLastFour.Error);
        Assert.Equal(CardErrors.InvalidPanFingerprint, invalidFingerprint.Error);
    }

    [Fact]
    public void Issue_NullCvcOrNonPositiveLimitOrDateMismatch_ReturnsFailure() {
        Result<CreditCard> nullCvc = CreditCard.Issue(
            "customer",
            "1111",
            new string('a', 64),
            null!,
            Money.Create(10_000m).Value,
            "admin",
            IssuedAt,
            IssueDate);

        Result<CreditCard> zeroLimit = CreditCard.Issue(
            "customer",
            "1111",
            new string('a', 64),
            CvcDigest.Create(new string('b', 64)).Value,
            Money.Create(0m).Value,
            "admin",
            IssuedAt,
            IssueDate);

        Result<CreditCard> dateMismatch = CreditCard.Issue(
            "customer",
            "1111",
            new string('a', 64),
            CvcDigest.Create(new string('b', 64)).Value,
            Money.Create(10_000m).Value,
            "admin",
            IssuedAt.AddDays(1),
            IssueDate);

        Assert.Equal(CardErrors.InvalidCvcDigest, nullCvc.Error);
        Assert.Equal(CardErrors.LimitMustBePositive, zeroLimit.Error);
        Assert.Equal(CardErrors.InconsistentIssueDate, dateMismatch.Error);
    }

    [Fact]
    public void AuthorizeCharge_CancelledCard_ReturnsNotActive() {
        CreditCard card = CreateCard();
        card.Cancel(IssuedAt.AddDays(1)).IsSuccess.Should().BeTrue();

        Result result = card.AuthorizeCharge(Money.Create(100m).Value, IssueDate.AddDays(2));

        Assert.Equal(CardErrors.NotActive, result.Error);
    }

    [Fact]
    public void AuthorizeCharge_NonPositiveAmount_ReturnsFailure() {
        CreditCard card = CreateCard();

        Result result = card.AuthorizeCharge(Money.Create(0m).Value, IssueDate);

        Assert.Equal(CardErrors.AmountMustBePositive, result.Error);
    }

    [Fact]
    public void ApplyPayment_NoDebt_ReturnsNoDebt() {
        CreditCard card = CreateCard();

        Result<Money> result = card.ApplyPayment(Money.Create(100m).Value);

        Assert.Equal(CardErrors.NoDebt, result.Error);
    }

    [Fact]
    public void ApplyPayment_NonPositiveAmount_ReturnsFailure() {
        CreditCard card = CreateCard();
        card.AuthorizeCharge(Money.Create(100m).Value, IssueDate);

        Result<Money> result = card.ApplyPayment(Money.Create(0m).Value);

        Assert.Equal(CardErrors.AmountMustBePositive, result.Error);
    }

    [Fact]
    public void ApplyPayment_CancelledCard_ReturnsNotActive() {
        CreditCard card = CreateCard();
        card.AuthorizeCharge(Money.Create(100m).Value, IssueDate);
        card.ApplyPayment(Money.Create(100m).Value).IsSuccess.Should().BeTrue();
        card.Cancel(IssuedAt.AddDays(1)).IsSuccess.Should().BeTrue();

        Result<Money> result = card.ApplyPayment(Money.Create(50m).Value);

        Assert.Equal(CardErrors.NotActive, result.Error);
    }

    private static CreditCard CreateCard() =>
        CreditCard.Issue(
            "customer",
            "1111",
            new string('a', 64),
            CvcDigest.Create(new string('b', 64)).Value,
            Money.Create(10_000m).Value,
            "admin",
            IssuedAt,
            IssueDate).Value;
}
