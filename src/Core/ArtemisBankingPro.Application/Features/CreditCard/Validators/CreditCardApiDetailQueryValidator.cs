using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.CreditCard.Validators;

public sealed class CreditCardApiDetailQueryValidator
    : AbstractValidator<GetCreditCardApiDetailQuery> {
    public CreditCardApiDetailQueryValidator() {
        RuleFor(query => query.CardId)
            .GreaterThan(0)
            .WithMessage("La tarjeta es requerida.");
    }
}
