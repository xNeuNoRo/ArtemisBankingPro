using ArtemisBankingPro.Application.Features.Cashier.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Cashier.Validators;

public sealed class GetCashierDashboardQueryValidator : AbstractValidator<GetCashierDashboardQuery> {
    public GetCashierDashboardQueryValidator() {
        RuleFor(x => x.CashierId)
            .NotEmpty()
            .WithMessage("El identificador del cajero es requerido.")
            .MaximumLength(450);

        RuleFor(x => x.Date)
            .NotEmpty()
            .WithMessage("La fecha es requerida.");
    }
}

public sealed class GetCashierOperationsPagedQueryValidator
    : AbstractValidator<GetCashierOperationsPagedQuery> {
    public GetCashierOperationsPagedQueryValidator() {
        RuleFor(x => x.CashierId)
            .NotEmpty()
            .WithMessage("El identificador del cajero es requerido.")
            .MaximumLength(450);

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");

        When(x => x.Filters is not null, () => {
            When(x => x.Filters!.Kind is not null, () => {
                RuleFor(x => x.Filters!.Kind!.Value)
                    .IsInEnum()
                    .WithMessage("El tipo de operación no es válido.");
            });

            When(x => x.Filters!.Status is not null, () => {
                RuleFor(x => x.Filters!.Status!.Value)
                    .IsInEnum()
                    .WithMessage("El estado no es válido.");
            });

            When(
                x => x.Filters!.FromDate is not null && x.Filters.ToDate is not null,
                () => {
                    RuleFor(x => x.Filters)
                        .Must(filters => filters!.FromDate <= filters.ToDate)
                        .WithMessage(
                            "La fecha inicial debe ser anterior o igual a la fecha final."
                        );
                }
            );
        });
    }
}
