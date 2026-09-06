using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Client.Validators;

public sealed class ProcessCashAdvanceCommandValidator
    : AbstractValidator<ProcessCashAdvanceCommand> {
    public ProcessCashAdvanceCommandValidator() {
        RuleFor(x => x.CardId).GreaterThan(0).WithMessage("La tarjeta es requerida.");
        RuleFor(x => x.DestinationAccountNumber)
            .NotEmpty()
            .WithMessage("La cuenta destino es requerida.")
            .MaximumLength(9);
        When(x => !string.IsNullOrWhiteSpace(x.DestinationAccountNumber), () => {
            RuleFor(x => x.DestinationAccountNumber)
                .Must(value => AccountNumber.Create(value).IsSuccess)
                .WithMessage(AccountErrors.InvalidNumber.Message);
        });
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("El monto del avance debe ser mayor que cero.");
    }
}

public sealed class GetCashAdvanceQuoteQueryValidator
    : AbstractValidator<GetCashAdvanceQuoteQuery> {
    public GetCashAdvanceQuoteQueryValidator() {
        RuleFor(x => x.CardId).GreaterThan(0).WithMessage("La tarjeta es requerida.");
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("El monto del avance debe ser mayor que cero.");
    }
}
