using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Common.Validation;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Users.Validators;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand> {
    private static readonly string[] AllowedRoles = ["Administrador", "Cajero", "Cliente"];

    public CreateUserCommandValidator() {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("El nombre es requerido.")
            .MaximumLength(100).WithMessage("El nombre no debe exceder 100 caracteres.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("El apellido es requerido.")
            .MaximumLength(100).WithMessage("El apellido no debe exceder 100 caracteres.");

        RuleFor(x => x.Identification)
            .NotEmpty().WithMessage("La cédula es requerida.")
            .MaximumLength(IdentityValidationLimits.IdentificationMaxLength)
            .WithMessage(IdentityValidationLimits.IdentificationMaxLengthMessage);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo electrónico es requerido.")
            .EmailAddress().WithMessage("El correo electrónico debe tener un formato válido.")
            .MaximumLength(200).WithMessage("El correo no debe exceder 200 caracteres.");

        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("El nombre de usuario es requerido.")
            .MaximumLength(50).WithMessage("El nombre de usuario no debe exceder 50 caracteres.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contraseña es requerida.")
            .MinimumLength(8).WithMessage("La contraseña debe tener al menos 8 caracteres.")
            .MaximumLength(128).WithMessage("La contraseña no debe exceder 128 caracteres.");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("La confirmación de la contraseña es requerida.")
            .Equal(x => x.Password).WithMessage("La contraseña y la confirmación de contraseña deben coincidir.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("El tipo de usuario es requerido.")
            .Must(role => AllowedRoles.Contains(role))
            .WithMessage("El tipo de usuario debe ser Administrador, Cajero o Cliente.");

        When(x => x.Role == "Cliente", () => {
            RuleFor(x => x.InitialAmount)
                .GreaterThanOrEqualTo(0)
                .WithMessage("El monto inicial no puede ser negativo.");
        });

        When(x => x.Role != "Cliente", () => {
            RuleFor(x => x.InitialAmount)
                .Null()
                .WithMessage("El monto inicial solo aplica para usuarios con rol Cliente.");
        });

        When(x => x.CallbackUrl is not null, () => {
            RuleFor(x => x.CallbackUrl)
                .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
                .WithMessage("La URL de retorno debe ser una URL absoluta válida.");
        });
    }
}
