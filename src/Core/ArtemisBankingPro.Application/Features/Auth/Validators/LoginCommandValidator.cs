using ArtemisBankingPro.Application.Features.Auth.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Auth.Validators;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand> {
    public LoginCommandValidator() {
        RuleFor(x => x.UserName)
            .NotEmpty()
            .WithMessage("El nombre de usuario es requerido.")
            .MaximumLength(50);

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("La contraseña es requerida.")
            .MaximumLength(128);
    }
}
