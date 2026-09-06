using ArtemisBankingPro.Application.Features.Auth.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Auth.Validators;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand> {
    public ResetPasswordCommandValidator() {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("El identificador del usuario es requerido.")
            .MaximumLength(450);

        RuleFor(x => x.Token)
            .NotEmpty()
            .WithMessage("El token es requerido.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("La contraseña es requerida.")
            .MinimumLength(8)
            .WithMessage("La contraseña debe tener al menos 8 caracteres.")
            .MaximumLength(128);

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty()
            .WithMessage("La confirmación de la contraseña es requerida.")
            .Equal(x => x.Password)
            .WithMessage("La contraseña y la confirmación de contraseña deben coincidir.");
    }
}
