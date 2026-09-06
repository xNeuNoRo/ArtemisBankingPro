using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Client.Validators;

public sealed class ProcessExpressTransactionCommandValidator
    : AbstractValidator<ProcessExpressTransactionCommand> {
    public ProcessExpressTransactionCommandValidator() {
        RuleFor(x => x.SourceAccountNumber)
            .NotEmpty()
            .WithMessage("La cuenta origen es requerida.")
            .MaximumLength(9);
        RuleFor(x => x.DestinationAccountNumber)
            .NotEmpty()
            .WithMessage("La cuenta destino es requerida.")
            .MaximumLength(9);

        When(x => !string.IsNullOrWhiteSpace(x.SourceAccountNumber), () => {
            RuleFor(x => x.SourceAccountNumber)
                .Must(value => AccountNumber.Create(value).IsSuccess)
                .WithMessage(AccountErrors.InvalidNumber.Message);
        });
        When(x => !string.IsNullOrWhiteSpace(x.DestinationAccountNumber), () => {
            RuleFor(x => x.DestinationAccountNumber)
                .Must(value => AccountNumber.Create(value).IsSuccess)
                .WithMessage(AccountErrors.InvalidNumber.Message);
        });

        RuleFor(x => x.DestinationAccountNumber)
            .NotEqual(x => x.SourceAccountNumber)
            .WithMessage("La cuenta destino no puede ser la misma cuenta de origen.");
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("El monto a transferir debe ser mayor que cero.");
    }
}

public sealed class ProcessClientCardPaymentCommandValidator
    : AbstractValidator<ProcessClientCardPaymentCommand> {
    public ProcessClientCardPaymentCommandValidator() {
        RuleFor(x => x.CardId).GreaterThan(0).WithMessage("La tarjeta es requerida.");
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
            .WithMessage("El monto del pago debe ser mayor que cero.");
    }
}

public sealed class ProcessClientLoanPaymentCommandValidator
    : AbstractValidator<ProcessClientLoanPaymentCommand> {
    public ProcessClientLoanPaymentCommandValidator() {
        RuleFor(x => x.LoanId).GreaterThan(0).WithMessage("El préstamo es requerido.");
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
            .WithMessage("El monto del pago debe ser mayor que cero.");
    }
}

public sealed class ProcessBeneficiaryTransferCommandValidator
    : AbstractValidator<ProcessBeneficiaryTransferCommand> {
    public ProcessBeneficiaryTransferCommandValidator() {
        RuleFor(x => x.BeneficiaryId)
            .GreaterThan(0)
            .WithMessage("El beneficiario es requerido.");
        RuleFor(x => x.SourceAccountNumber)
            .NotEmpty()
            .WithMessage("La cuenta origen es requerida.")
            .MaximumLength(9);
        When(x => !string.IsNullOrWhiteSpace(x.SourceAccountNumber), () => {
            RuleFor(x => x.SourceAccountNumber)
                .Must(value => AccountNumber.Create(value).IsSuccess)
                .WithMessage(AccountErrors.InvalidNumber.Message);
        });
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("El monto a transferir debe ser mayor que cero.");
    }
}

public sealed class ProcessOwnAccountsTransferCommandValidator
    : AbstractValidator<ProcessOwnAccountsTransferCommand> {
    public ProcessOwnAccountsTransferCommandValidator() {
        RuleFor(x => x.SourceAccountNumber)
            .NotEmpty()
            .WithMessage("La cuenta origen es requerida.")
            .MaximumLength(9);
        RuleFor(x => x.DestinationAccountNumber)
            .NotEmpty()
            .WithMessage("La cuenta destino es requerida.")
            .MaximumLength(9);

        When(x => !string.IsNullOrWhiteSpace(x.SourceAccountNumber), () => {
            RuleFor(x => x.SourceAccountNumber)
                .Must(value => AccountNumber.Create(value).IsSuccess)
                .WithMessage(AccountErrors.InvalidNumber.Message);
        });
        When(x => !string.IsNullOrWhiteSpace(x.DestinationAccountNumber), () => {
            RuleFor(x => x.DestinationAccountNumber)
                .Must(value => AccountNumber.Create(value).IsSuccess)
                .WithMessage(AccountErrors.InvalidNumber.Message);
        });

        RuleFor(x => x.DestinationAccountNumber)
            .NotEqual(x => x.SourceAccountNumber)
            .WithMessage("La cuenta de origen y la cuenta de destino no pueden ser la misma.");
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("El monto a transferir debe ser mayor que cero.");
    }
}
