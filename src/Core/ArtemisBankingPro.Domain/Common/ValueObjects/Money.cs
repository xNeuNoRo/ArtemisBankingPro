using System.Globalization;

namespace ArtemisBankingPro.Domain.Common.ValueObjects;

public sealed record Money : IComparable<Money> {
    private Money(decimal amount) {
        Amount = amount;
    }

    public decimal Amount { get; }

    public static Money Zero { get; } = new(0m);

    public static Result<Money> Create(decimal amount) {
        if (amount < 0m) {
            return Result.Failure<Money>(
                DomainError.Validation("Money.Negative", "El monto no puede ser negativo.")
            );
        }

        return Result.Success(FromDecimal(amount));
    }

    public Money Add(Money other) => FromDecimal(Amount + other.Amount);

    public Result<Money> Subtract(Money other) {
        if (other.Amount > Amount) {
            return Result.Failure<Money>(
                DomainError.Declined("Money.Insufficient", "El monto no puede volverse negativo.")
            );
        }

        return Result.Success(FromDecimal(Amount - other.Amount));
    }

    public Money Multiply(decimal multiplier) {
        if (multiplier < 0m) {
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        }

        return FromDecimal(Amount * multiplier);
    }

    public int CompareTo(Money? other) => other is null ? 1 : Amount.CompareTo(other.Amount);

    public override string ToString() => Amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

    internal static Money FromDecimal(decimal amount) {
        if (amount < 0m) {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        return new Money(Round(amount));
    }

    internal static decimal Round(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
