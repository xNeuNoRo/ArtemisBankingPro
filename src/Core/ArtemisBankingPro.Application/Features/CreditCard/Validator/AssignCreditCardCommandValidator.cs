using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.CreditCard.Validator;

public sealed class AssignCreditCardCommandValidator : AbstractValidator<AssignCreditCardCommand> {
    public AssignCreditCardCommandValidator() {
        RuleFor(x => x.CustomerUserId)
            .NotEmpty()
            .WithMessage("El cliente es requerido.")
            .MaximumLength(450)
            .WithMessage("El identificador del cliente no puede exceder 450 caracteres.");

        RuleFor(x => x.CreditLimit)
            .GreaterThan(0m)
            .WithMessage("El límite de crédito debe ser mayor a cero.");
    }
}
