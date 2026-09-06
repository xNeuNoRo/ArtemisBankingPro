using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Lending.Policies;
using FluentValidation;
using ArtemisBankingPro.Application.Common.Validation;

namespace ArtemisBankingPro.Application.Features.Loans.Validators;

public sealed class CreateLoanCommandValidator : AbstractValidator<CreateLoanCommand> {
    public CreateLoanCommandValidator() {
        RuleFor(x => x.CustomerUserId)
            .NotEmpty()
            .WithMessage("El cliente es requerido.")
            .MaximumLength(450);

        RuleFor(x => x.CapitalAmount)
            .GreaterThan(0)
            .WithMessage("El monto a prestar debe ser mayor que cero.");

        RuleFor(x => x.TermMonths)
            .Must(AmortizationCalculator.IsValidTerm)
            .WithMessage("El plazo seleccionado no es válido.");

        RuleFor(x => x.AnnualInterestRate)
            .GreaterThanOrEqualTo(0)
            .WithMessage("La tasa de interés anual no puede ser negativa.");
    }
}

public sealed class UpdateLoanRateCommandValidator : AbstractValidator<UpdateLoanRateCommand> {
    public UpdateLoanRateCommandValidator() {
        RuleFor(x => x.LoanId)
            .GreaterThan(0)
            .WithMessage("El préstamo es requerido.");

        RuleFor(x => x.AnnualInterestRate)
            .GreaterThanOrEqualTo(0)
            .WithMessage("La tasa de interés anual no puede ser negativa.");
    }
}

public sealed class GetLoansPagedQueryValidator : AbstractValidator<GetLoansPagedQuery> {
    private static readonly string[] AllowedStatuses = ["activos", "completados", "todos"];

    public GetLoansPagedQueryValidator() {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");

        When(x => x.Status is not null, () => {
            RuleFor(x => x.Status)
                .Must(status => AllowedStatuses.Contains(status!))
                .WithMessage("El estado debe ser activos, completados o todos.");
        });

        When(x => x.Identification is not null, () => {
            RuleFor(x => x.Identification)
                .MaximumLength(IdentityValidationLimits.IdentificationMaxLength)
                .WithMessage(IdentityValidationLimits.IdentificationMaxLengthMessage);
        });
    }
}

public sealed class GetLoanDetailQueryValidator : AbstractValidator<GetLoanDetailQuery> {
    public GetLoanDetailQueryValidator() {
        RuleFor(x => x.LoanId)
            .GreaterThan(0)
            .WithMessage("El préstamo es requerido.");
    }
}
