using ArtemisBankingPro.Application.Features.Merchants.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Merchants.Validators;

/// <summary>
/// Validación de forma de la actualización de comercios con mensajes del spec §40.
/// El RNC dominicano tiene exactamente 9 dígitos. La unicidad de RNC y correo
/// es un invariante de estado y se revalida dentro del caso de uso protegido
/// y en la base de datos.
/// </summary>
public sealed class UpdateMerchantCommandValidator : AbstractValidator<UpdateMerchantCommand> {
    public UpdateMerchantCommandValidator() {
        RuleFor(x => x.MerchantId)
            .GreaterThan(0)
            .WithMessage("El identificador del comercio es obligatorio.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("El nombre del comercio es obligatorio.")
            .MaximumLength(120)
            .WithMessage("El nombre del comercio no debe exceder 120 caracteres.");

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .WithMessage("La descripción no debe exceder 500 caracteres.");

        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("El correo electrónico es obligatorio.")
            .EmailAddress()
            .WithMessage("El correo electrónico debe tener un formato válido.")
            .MaximumLength(320)
            .WithMessage("El correo electrónico no debe exceder 320 caracteres.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("El teléfono es obligatorio.")
            .MaximumLength(20)
            .WithMessage("El teléfono no debe exceder 20 caracteres.");

        RuleFor(x => x.Rnc)
            .NotEmpty()
            .WithMessage("El RNC es obligatorio.")
            .Length(9)
            .WithMessage("El RNC debe tener 9 caracteres.");

        RuleFor(x => x.Rnc)
            .Must(rnc => rnc is not null && rnc.All(char.IsDigit))
            .WithMessage("El RNC debe contener solo dígitos.");
    }
}
