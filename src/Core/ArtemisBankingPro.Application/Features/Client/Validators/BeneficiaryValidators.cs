using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Client.Validators;

public sealed class AddBeneficiaryCommandValidator : AbstractValidator<AddBeneficiaryCommand> {
    public AddBeneficiaryCommandValidator() {
        RuleFor(x => x.DestinationAccountNumber)
            .NotEmpty()
            .WithMessage("La cuenta del beneficiario es requerida.")
            .MaximumLength(9);

        When(x => !string.IsNullOrWhiteSpace(x.DestinationAccountNumber), () => {
            RuleFor(x => x.DestinationAccountNumber)
                .Must(value => AccountNumber.Create(value).IsSuccess)
                .WithMessage(AccountErrors.InvalidNumber.Message);
        });
    }
}

public sealed class RemoveBeneficiaryCommandValidator
    : AbstractValidator<RemoveBeneficiaryCommand> {
    public RemoveBeneficiaryCommandValidator() {
        RuleFor(x => x.BeneficiaryId)
            .GreaterThan(0)
            .WithMessage("El beneficiario es requerido.");
    }
}
