using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Common.Validation;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Users.Validators;

public sealed class CreateCommerceUserCommandValidator : AbstractValidator<CreateCommerceUserCommand> {
    public CreateCommerceUserCommandValidator() {
        RuleFor(x => x.CommerceId)
            .GreaterThan(0).WithMessage("El comercio es requerido.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("El nombre es requerido.")
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("El apellido es requerido.")
            .MaximumLength(100);

        RuleFor(x => x.Identification)
            .NotEmpty().WithMessage("La cédula es requerida.")
            .MaximumLength(IdentityValidationLimits.IdentificationMaxLength);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo electrónico es requerido.")
            .EmailAddress().WithMessage("El correo electrónico debe tener un formato válido.")
            .MaximumLength(200);

        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("El nombre de usuario es requerido.")
            .MaximumLength(50);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contraseña es requerida.")
            .MinimumLength(8).WithMessage("La contraseña debe tener al menos 8 caracteres.")
            .MaximumLength(128);

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("La confirmación de la contraseña es requerida.")
            .Equal(x => x.Password)
            .WithMessage("La contraseña y la confirmación de contraseña deben coincidir.");

        RuleFor(x => x.InitialAmount)
            .NotNull().WithMessage("El monto inicial es requerido.")
            .GreaterThanOrEqualTo(0)
            .WithMessage("El balance inicial no puede ser negativo.");

        When(x => x.CallbackUrl is not null, () => {
            RuleFor(x => x.CallbackUrl)
                .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
                .WithMessage("La URL de retorno debe ser una URL absoluta válida.");
        });
    }
}
