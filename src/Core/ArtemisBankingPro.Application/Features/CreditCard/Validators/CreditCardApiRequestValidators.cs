using ArtemisBankingPro.Application.Features.CreditCard.Requests;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.CreditCard.Validators;

public sealed class AssignCreditCardApiRequestValidator
    : AbstractValidator<AssignCreditCardApiRequest> {
    public AssignCreditCardApiRequestValidator() {
        RuleFor(request => request.ClientId)
            .NotEmpty()
            .WithMessage("El cliente es requerido.")
            .MaximumLength(450);
        RuleFor(request => request.CreditLimit)
            .NotNull()
            .WithMessage("El límite de crédito es requerido.");
        When(
            request => request.CreditLimit.HasValue,
            () => RuleFor(request => request.CreditLimit)
                .GreaterThan(0m)
                .WithMessage("El límite de crédito debe ser mayor a cero.")
        );
    }
}

public sealed class UpdateCreditCardLimitApiRequestValidator
    : AbstractValidator<UpdateCreditCardLimitApiRequest> {
    public UpdateCreditCardLimitApiRequestValidator() {
        RuleFor(request => request.CreditLimit)
            .NotNull()
            .WithMessage("El límite de crédito es requerido.");
        When(
            request => request.CreditLimit.HasValue,
            () => RuleFor(request => request.CreditLimit)
                .GreaterThan(0m)
                .WithMessage("El nuevo límite debe ser mayor a cero.")
        );
    }
}
