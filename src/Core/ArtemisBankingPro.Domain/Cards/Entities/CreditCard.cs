using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Cards.Entities;

public sealed class CreditCard : Entity<int> {
    private CreditCard() { }

    private CreditCard(
        string customerUserId,
        CardNumber number,
        CvcDigest cvcDigest,
        Money creditLimit,
        CardExpiration expiration,
        string assignedByUserId,
        DateTimeOffset issuedAt
    ) {
        CustomerUserId = customerUserId;
        Number = number;
        CvcDigest = cvcDigest;
        CreditLimit = creditLimit;
        CurrentDebt = Money.Zero;
        Expiration = expiration;
        AssignedByUserId = assignedByUserId;
        IssuedAt = issuedAt;
        Status = CreditCardStatus.Active;
    }

    public string CustomerUserId { get; private set; } = null!;

    public CardNumber Number { get; private set; } = null!;

    public CvcDigest CvcDigest { get; private set; } = null!;

    public Money CreditLimit { get; private set; } = Money.Zero;

    public Money CurrentDebt { get; private set; } = Money.Zero;

    public Money AvailableCredit => CreditLimit.Subtract(CurrentDebt).Value;

    public CardExpiration Expiration { get; private set; } = null!;

    public CreditCardStatus Status { get; private set; }

    public string AssignedByUserId { get; private set; } = null!;

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public static Result<CreditCard> Issue(
        string customerUserId,
        CardNumber number,
        CvcDigest cvcDigest,
        Money creditLimit,
        string assignedByUserId,
        DateTimeOffset issuedAt,
        DateOnly businessDate
    ) {
        if (string.IsNullOrWhiteSpace(customerUserId)) {
            return Result.Failure<CreditCard>(CardErrors.InvalidCustomer);
        }

        if (string.IsNullOrWhiteSpace(assignedByUserId)) {
            return Result.Failure<CreditCard>(CardErrors.InvalidAssigner);
        }

        if (number is null || cvcDigest is null || creditLimit is null) {
            return Result.Failure<CreditCard>(CardErrors.InvalidCvcDigest);
        }

        if (creditLimit.Amount <= 0m) {
            return Result.Failure<CreditCard>(CardErrors.LimitMustBePositive);
        }

        if (DateOnly.FromDateTime(issuedAt.DateTime) != businessDate) {
            return Result.Failure<CreditCard>(CardErrors.InconsistentIssueDate);
        }

        return Result.Success(
            new CreditCard(
                customerUserId,
                number,
                cvcDigest,
                creditLimit,
                CardExpiration.FromIssueDate(businessDate),
                assignedByUserId,
                issuedAt
            )
        );
    }

    public Result CanAuthorizeCharge(Money amount, DateOnly businessDate) {
        if (Status != CreditCardStatus.Active) {
            return Result.Failure(CardErrors.NotActive);
        }

        if (Expiration.IsExpired(businessDate)) {
            return Result.Failure(CardErrors.Expired);
        }

        if (amount is null || amount.Amount <= 0m) {
            return Result.Failure(CardErrors.AmountMustBePositive);
        }

        return amount <= AvailableCredit
            ? Result.Success()
            : Result.Failure(CardErrors.InsufficientCredit);
    }

    public Result AuthorizeCharge(Money amount, DateOnly businessDate) {
        Result validation = CanAuthorizeCharge(amount, businessDate);
        if (validation.IsFailure) {
            return validation;
        }

        CurrentDebt = CurrentDebt.Add(amount);
        return Result.Success();
    }

    public Result<Money> ApplyPayment(Money requestedAmount) {
        if (Status != CreditCardStatus.Active) {
            return Result.Failure<Money>(CardErrors.NotActive);
        }

        if (requestedAmount is null || requestedAmount.Amount <= 0m) {
            return Result.Failure<Money>(CardErrors.AmountMustBePositive);
        }

        if (CurrentDebt == Money.Zero) {
            return Result.Failure<Money>(CardErrors.NoDebt);
        }

        Money appliedAmount = requestedAmount <= CurrentDebt ? requestedAmount : CurrentDebt;
        CurrentDebt = CurrentDebt.Subtract(appliedAmount).Value;
        return Result.Success(appliedAmount);
    }

    public Result ChangeCreditLimit(Money newLimit) {
        if (Status != CreditCardStatus.Active) {
            return Result.Failure(CardErrors.NotActive);
        }

        if (newLimit is null || newLimit.Amount <= 0m) {
            return Result.Failure(CardErrors.LimitMustBePositive);
        }

        if (newLimit < CurrentDebt) {
            return Result.Failure(CardErrors.LimitBelowDebt);
        }

        CreditLimit = newLimit;
        return Result.Success();
    }

    public Result Cancel(DateTimeOffset cancelledAt) {
        if (Status != CreditCardStatus.Active) {
            return Result.Failure(CardErrors.NotActive);
        }

        if (CurrentDebt != Money.Zero) {
            return Result.Failure(CardErrors.DebtMustBeZero);
        }

        if (cancelledAt < IssuedAt) {
            return Result.Failure(CardErrors.InvalidCancellationDate);
        }

        Status = CreditCardStatus.Cancelled;
        CancelledAt = cancelledAt;
        return Result.Success();
    }
}
