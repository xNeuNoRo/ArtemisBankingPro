using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Validators;

public sealed class CancelSecondarySavingsAccountCommandValidator
    : AbstractValidator<CancelSecondarySavingsAccountCommand> {
    public CancelSecondarySavingsAccountCommandValidator() {
        RuleFor(command => command.AccountNumber)
            .Matches("^[0-9]{9}$")
            .WithMessage("El número de cuenta debe contener exactamente 9 dígitos.");
    }
}
