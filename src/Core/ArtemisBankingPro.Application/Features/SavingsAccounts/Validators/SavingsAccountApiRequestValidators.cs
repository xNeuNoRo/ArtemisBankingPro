using ArtemisBankingPro.Application.Features.SavingsAccounts.Requests;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Validators;

public sealed class AssignSavingsAccountApiRequestValidator
    : AbstractValidator<AssignSavingsAccountApiRequest> {
    public AssignSavingsAccountApiRequestValidator() {
        RuleFor(request => request.ClientId)
            .NotEmpty()
            .WithMessage("El cliente es requerido.")
            .MaximumLength(450)
            .WithMessage("El identificador del cliente no puede exceder 450 caracteres.");

        RuleFor(request => request.InitialBalance)
            .NotNull()
            .WithMessage("El balance inicial es requerido.");

        When(
            request => request.InitialBalance.HasValue,
            () => RuleFor(request => request.InitialBalance)
                .GreaterThanOrEqualTo(0m)
                .WithMessage("El balance inicial no puede ser negativo.")
        );
    }
}
