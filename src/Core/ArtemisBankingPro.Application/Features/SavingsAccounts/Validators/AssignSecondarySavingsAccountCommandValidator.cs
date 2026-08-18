using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Validators;

public sealed class AssignSecondarySavingsAccountCommandValidator
    : AbstractValidator<AssignSecondarySavingsAccountCommand> {
    public AssignSecondarySavingsAccountCommandValidator() {
        RuleFor(command => command.CustomerUserId)
            .NotEmpty()
            .WithMessage("El cliente es requerido.")
            .MaximumLength(450)
            .WithMessage("El identificador del cliente no puede exceder 450 caracteres.");

        RuleFor(command => command.InitialAmount)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("El monto inicial no puede ser negativo.");
    }
}
