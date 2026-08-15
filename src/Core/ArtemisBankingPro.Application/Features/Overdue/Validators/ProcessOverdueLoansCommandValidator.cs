using ArtemisBankingPro.Application.Features.Overdue.Commands;
using ArtemisBankingPro.Application.Interfaces.Time;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Overdue.Validators;

public sealed class ProcessOverdueLoansCommandValidator
    : AbstractValidator<ProcessOverdueLoansCommand> {
    public ProcessOverdueLoansCommandValidator(IBusinessClock clock) {
        RuleFor(command => command.BusinessDate)
            .NotEqual(default(DateOnly))
            .WithMessage("La fecha de negocio es requerida.")
            .Equal(clock.Today)
            .WithMessage("La fecha indicada debe coincidir con la fecha de negocio actual.");
        RuleFor(command => command.BatchSize)
            .GreaterThan(0)
            .WithMessage("El tamaño del lote debe ser mayor que cero.");
    }
}
