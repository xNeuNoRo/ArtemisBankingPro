using ArtemisBankingPro.Application.Features.Cashier.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Cashier.Validators;

public sealed class GetCashierOperationsQueryValidator : AbstractValidator<GetCashierOperationsQuery> {
    private static readonly string[] AllowedOperationTypes =
        ["Deposit", "Withdrawal", "CardPayment", "LoanPayment", "ThirdPartyTransfer"];

    public GetCashierOperationsQueryValidator() {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");

        When(x => x.OperationType is not null, () => {
            RuleFor(x => x.OperationType)
                .Must(operationType =>
                    AllowedOperationTypes.Contains(operationType!, StringComparer.OrdinalIgnoreCase)
                )
                .WithMessage(
                    "El tipo de operación debe ser Deposit, Withdrawal, CardPayment, "
                        + "LoanPayment o ThirdPartyTransfer."
                );
        });

        When(x => x.DateFrom is not null && x.DateTo is not null, () => {
            RuleFor(x => x)
                .Must(query => query.DateFrom <= query.DateTo)
                .WithMessage("La fecha inicial debe ser anterior o igual a la fecha final.");
        });
    }
}
