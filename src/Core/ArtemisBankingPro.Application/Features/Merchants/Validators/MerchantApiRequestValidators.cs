using ArtemisBankingPro.Application.Features.Merchants.Requests;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Merchants.Validators;

public sealed class CreateMerchantApiRequestValidator
    : AbstractValidator<CreateMerchantApiRequest> {
    public CreateMerchantApiRequestValidator() {
        RuleFor(request => request.Name)
            .NotEmpty()
            .WithMessage("El nombre del comercio es obligatorio.")
            .MaximumLength(120)
            .WithMessage("El nombre del comercio no debe exceder 120 caracteres.");
        RuleFor(request => request.Description)
            .MaximumLength(500)
            .WithMessage("La descripción no debe exceder 500 caracteres.");
        RuleFor(request => request.Email)
            .NotEmpty()
            .WithMessage("El correo electrónico es obligatorio.")
            .EmailAddress()
            .WithMessage("El correo electrónico debe tener un formato válido.")
            .MaximumLength(320)
            .WithMessage("El correo electrónico no debe exceder 320 caracteres.");
        RuleFor(request => request.PhoneNumber)
            .NotEmpty()
            .WithMessage("El teléfono es obligatorio.")
            .MaximumLength(20)
            .WithMessage("El teléfono no debe exceder 20 caracteres.");
        RuleFor(request => request.Rnc)
            .NotEmpty()
            .WithMessage("El RNC es obligatorio.")
            .Length(9)
            .WithMessage("El RNC debe tener 9 caracteres.");
        RuleFor(request => request.Rnc)
            .Must(rnc => rnc is not null && rnc.All(char.IsDigit))
            .WithMessage("El RNC debe contener solo dígitos.");
    }
}

public sealed class UpdateMerchantApiRequestValidator
    : AbstractValidator<UpdateMerchantApiRequest> {
    public UpdateMerchantApiRequestValidator() {
        RuleFor(request => request.Name)
            .NotEmpty()
            .WithMessage("El nombre del comercio es obligatorio.")
            .MaximumLength(120)
            .WithMessage("El nombre del comercio no debe exceder 120 caracteres.");
        RuleFor(request => request.Description)
            .MaximumLength(500)
            .WithMessage("La descripción no debe exceder 500 caracteres.");
        RuleFor(request => request.Email)
            .NotEmpty()
            .WithMessage("El correo electrónico es obligatorio.")
            .EmailAddress()
            .WithMessage("El correo electrónico debe tener un formato válido.")
            .MaximumLength(320)
            .WithMessage("El correo electrónico no debe exceder 320 caracteres.");
        RuleFor(request => request.PhoneNumber)
            .NotEmpty()
            .WithMessage("El teléfono es obligatorio.")
            .MaximumLength(20)
            .WithMessage("El teléfono no debe exceder 20 caracteres.");
        RuleFor(request => request.Rnc)
            .NotEmpty()
            .WithMessage("El RNC es obligatorio.")
            .Length(9)
            .WithMessage("El RNC debe tener 9 caracteres.");
        RuleFor(request => request.Rnc)
            .Must(rnc => rnc is not null && rnc.All(char.IsDigit))
            .WithMessage("El RNC debe contener solo dígitos.");
    }
}

public sealed class ChangeMerchantStatusApiRequestValidator
    : AbstractValidator<ChangeMerchantStatusApiRequest> {
    public ChangeMerchantStatusApiRequestValidator() {
        RuleFor(request => request.Status)
            .NotNull()
            .WithMessage("El campo status es obligatorio.");
    }
}
