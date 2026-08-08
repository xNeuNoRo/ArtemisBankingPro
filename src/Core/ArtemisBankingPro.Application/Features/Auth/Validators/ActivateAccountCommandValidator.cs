using ArtemisBankingPro.Application.Features.Auth.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Auth.Validators;

public sealed class ActivateAccountCommandValidator : AbstractValidator<ActivateAccountCommand> {
    public ActivateAccountCommandValidator() {
        RuleFor(x => x.Token)
            .NotEmpty()
            .WithMessage("El token es requerido.")
            .MaximumLength(256);
    }
}
