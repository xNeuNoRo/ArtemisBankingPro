using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Client.Validators;

public sealed class GetMyAccountTransactionsQueryValidator
    : AbstractValidator<GetMyAccountTransactionsQuery> {
    public GetMyAccountTransactionsQueryValidator() {
        RuleFor(x => x.AccountNumber)
            .Matches("^[0-9]{9}$")
            .WithMessage("El número de cuenta debe contener exactamente 9 dígitos.");
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");
        RuleFor(x => x.TransactionType)
            .NotEmpty()
            .When(x => x.TransactionType is not null)
            .WithMessage("El tipo de transacción no puede estar vacío.");
        RuleFor(x => x.TransactionType)
            .Must(type => type is "CRÉDITO" or "DÉBITO")
            .When(x => !string.IsNullOrWhiteSpace(x.TransactionType))
            .WithMessage("El tipo de transacción debe ser crédito o débito.");
        When(x => x.DateFrom is not null && x.DateTo is not null, () => {
            RuleFor(x => x)
                .Must(query => query.DateFrom <= query.DateTo)
                .WithMessage("La fecha inicial debe ser anterior o igual a la fecha final.");
        });
    }
}

public sealed class GetMyLoanDetailQueryValidator : AbstractValidator<GetMyLoanDetailQuery> {
    public GetMyLoanDetailQueryValidator() {
        RuleFor(x => x.LoanId).GreaterThan(0).WithMessage("El préstamo es requerido.");
    }
}

public sealed class GetMyCardDetailQueryValidator : AbstractValidator<GetMyCardDetailQuery> {
    public GetMyCardDetailQueryValidator() {
        RuleFor(x => x.CardId).GreaterThan(0).WithMessage("La tarjeta es requerida.");
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");
    }
}
