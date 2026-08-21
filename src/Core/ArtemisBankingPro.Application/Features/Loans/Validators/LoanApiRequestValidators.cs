using ArtemisBankingPro.Application.Features.Loans.Requests;
using ArtemisBankingPro.Domain.Lending.Policies;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Loans.Validators;

/// <summary>
/// Validates the transport contract before nullable API fields are translated
/// into the non-nullable application command.
/// </summary>
public sealed class CreateLoanRequestValidator : AbstractValidator<CreateLoanRequest> {
    public CreateLoanRequestValidator() {
        RuleFor(request => request.ClientId)
            .NotEmpty()
            .WithMessage("El cliente es requerido.")
            .MaximumLength(450);

        RuleFor(request => request.CapitalAmount)
            .NotNull()
            .WithMessage("El monto a prestar es requerido.");
        When(
            request => request.CapitalAmount.HasValue,
            () => RuleFor(request => request.CapitalAmount)
                .GreaterThan(0)
                .WithMessage("El monto a prestar debe ser mayor que cero.")
        );

        RuleFor(request => request.TermInMonths)
            .NotNull()
            .WithMessage("El plazo del préstamo es requerido.");
        When(
            request => request.TermInMonths.HasValue,
            () => RuleFor(request => request.TermInMonths)
                .Must(term => term.HasValue && AmortizationCalculator.IsValidTerm(term.Value))
                .WithMessage("El plazo seleccionado no es válido.")
        );

        RuleFor(request => request.AnnualInterestRate)
            .NotNull()
            .WithMessage("La tasa de interés anual es requerida.");
        When(
            request => request.AnnualInterestRate.HasValue,
            () => RuleFor(request => request.AnnualInterestRate)
                .GreaterThanOrEqualTo(0)
                .WithMessage("La tasa de interés anual no puede ser negativa.")
        );
    }
}

public sealed class UpdateLoanRateRequestValidator : AbstractValidator<UpdateLoanRateRequest> {
    public UpdateLoanRateRequestValidator() {
        RuleFor(request => request.AnnualInterestRate)
            .NotNull()
            .WithMessage("La tasa de interés anual es requerida.");
        When(
            request => request.AnnualInterestRate.HasValue,
            () => RuleFor(request => request.AnnualInterestRate)
                .GreaterThanOrEqualTo(0)
                .WithMessage("La tasa de interés anual no puede ser negativa.")
        );
    }
}
