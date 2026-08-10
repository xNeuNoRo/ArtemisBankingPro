using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.CreditCard.Validator;

public sealed class UpdateCardLimitCommandValidator : AbstractValidator<UpdateCardLimitCommand> {
    public UpdateCardLimitCommandValidator() {
        RuleFor(x => x.CardId)
            .GreaterThan(0)
            .WithMessage("La tarjeta es requerida.");

        RuleFor(x => x.NewLimit)
            .GreaterThan(0m)
            .WithMessage("El nuevo límite debe ser mayor a cero.");
    }
}
