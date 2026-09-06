using ArtemisBankingPro.Application.Features.Merchants.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Merchants.Validators;

/// <summary>
/// Validación de forma del cambio de estado de comercios.
/// </summary>
public sealed class ChangeMerchantStatusCommandValidator
    : AbstractValidator<ChangeMerchantStatusCommand> {
    public ChangeMerchantStatusCommandValidator() {
        RuleFor(x => x.MerchantId)
            .GreaterThan(0)
            .WithMessage("El identificador del comercio es obligatorio.");
    }
}
