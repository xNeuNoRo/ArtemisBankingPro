using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Cashier.Validators;

public sealed class ProcessDepositCommandValidator
    : AbstractValidator<ProcessDepositCommand> {
    public ProcessDepositCommandValidator() {
        RuleFor(x => x.AccountNumber)
            .NotEmpty()
            .WithMessage("La cuenta destino es requerida.")
            .MaximumLength(9);

        When(x => !string.IsNullOrWhiteSpace(x.AccountNumber), () => {
            RuleFor(x => x.AccountNumber)
                .Must(value => AccountNumber.Create(value).IsSuccess)
                .WithMessage(AccountErrors.InvalidNumber.Message);
        });

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("El monto del depósito debe ser mayor que cero.");
    }
}
