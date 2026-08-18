using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Cashier.Validators;

public sealed class ProcessCardPaymentCommandValidator
    : AbstractValidator<ProcessCardPaymentCommand> {
    public ProcessCardPaymentCommandValidator() {
        RuleFor(x => x.CardId)
            .GreaterThan(0)
            .WithMessage("Debe indicar la tarjeta a pagar.");

        RuleFor(x => x.AccountNumber)
            .NotEmpty()
            .WithMessage("La cuenta origen es requerida.")
            .MaximumLength(9);

        When(x => !string.IsNullOrWhiteSpace(x.AccountNumber), () => {
            RuleFor(x => x.AccountNumber)
                .Must(value => AccountNumber.Create(value).IsSuccess)
                .WithMessage(AccountErrors.InvalidNumber.Message);
        });

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("El monto a pagar debe ser mayor que cero.");
    }
}
