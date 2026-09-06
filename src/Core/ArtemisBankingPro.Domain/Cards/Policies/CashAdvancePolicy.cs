using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Cards.Policies;

/// <summary>
/// Representa la política de cálculo de un avance de efectivo en una tarjeta de crédito, incluyendo la tasa de interés y el cálculo del cargo total.
/// </summary>
public static class CashAdvancePolicy {
    public const decimal InterestRate = 0.0625m;

    public static Result<CashAdvanceQuote> Calculate(Money principal) {
        if (principal.Amount <= 0m) {
            return Result.Failure<CashAdvanceQuote>(CardErrors.AmountMustBePositive);
        }

        Money interest = principal.Multiply(InterestRate);
        return Result.Success(new CashAdvanceQuote(principal, interest, principal.Add(interest)));
    }
}
