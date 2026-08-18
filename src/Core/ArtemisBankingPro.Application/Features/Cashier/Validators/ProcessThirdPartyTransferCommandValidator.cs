using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Cashier.Validators;

public sealed class ProcessThirdPartyTransferCommandValidator
    : AbstractValidator<ProcessThirdPartyTransferCommand> {
    public ProcessThirdPartyTransferCommandValidator() {
        RuleFor(x => x.SourceAccountNumber)
            .NotEmpty()
            .WithMessage("La cuenta origen es requerida.")
            .MaximumLength(9);

        When(x => !string.IsNullOrWhiteSpace(x.SourceAccountNumber), () => {
            RuleFor(x => x.SourceAccountNumber)
                .Must(value => AccountNumber.Create(value).IsSuccess)
                .WithMessage(AccountErrors.InvalidNumber.Message);
        });

        RuleFor(x => x.DestinationAccountNumber)
            .NotEmpty()
            .WithMessage("La cuenta destino es requerida.")
            .MaximumLength(9);

        When(x => !string.IsNullOrWhiteSpace(x.DestinationAccountNumber), () => {
            RuleFor(x => x.DestinationAccountNumber)
                .Must(value => AccountNumber.Create(value).IsSuccess)
                .WithMessage(AccountErrors.InvalidNumber.Message);
        });

        RuleFor(x => x.DestinationAccountNumber)
            .NotEqual(x => x.SourceAccountNumber)
            .WithMessage("La cuenta origen y la cuenta destino no pueden ser la misma.");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("El monto de la transacción debe ser mayor que cero.");
    }
}
