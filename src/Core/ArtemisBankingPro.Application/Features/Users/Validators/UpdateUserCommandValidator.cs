using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Common.Validation;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Users.Validators;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand> {
    public UpdateUserCommandValidator() {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("El identificador del usuario es requerido.")
            .MaximumLength(450);

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("El nombre es requerido.")
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("El apellido es requerido.")
            .MaximumLength(100);

        RuleFor(x => x.Identification)
            .NotEmpty().WithMessage("La cédula es requerida.")
            .MaximumLength(IdentityValidationLimits.IdentificationMaxLength)
            .WithMessage(IdentityValidationLimits.IdentificationMaxLengthMessage);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo electrónico es requerido.")
            .EmailAddress().WithMessage("El correo electrónico debe tener un formato válido.")
            .MaximumLength(200);

        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("El nombre de usuario es requerido.")
            .MaximumLength(50);

        When(x => x.Password is not null and not "", () => {
            RuleFor(x => x.Password)
                .MinimumLength(8).WithMessage("La contraseña debe tener al menos 8 caracteres.")
                .MaximumLength(128);

            RuleFor(x => x.ConfirmPassword)
                .NotEmpty().WithMessage("Debe confirmar la nueva contraseña.")
                .Equal(x => x.Password)
                .WithMessage("La contraseña y la confirmación de contraseña deben coincidir.");
        });

        When(x => x.AdditionalAmount is not null, () => {
            RuleFor(x => x.AdditionalAmount)
                .GreaterThanOrEqualTo(0)
                .WithMessage("El monto adicional no puede ser negativo.");
        });
    }
}
