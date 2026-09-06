using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.CreditCard.Validator;

public sealed class CancelCreditCardCommandValidator : AbstractValidator<CancelCreditCardCommand> {
    public CancelCreditCardCommandValidator() {
        RuleFor(x => x.CardId)
            .GreaterThan(0)
            .WithMessage("La tarjeta es requerida.");
    }
}
