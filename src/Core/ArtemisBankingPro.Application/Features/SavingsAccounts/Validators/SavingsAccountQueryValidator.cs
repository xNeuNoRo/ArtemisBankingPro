using ArtemisBankingPro.Application.Features.SavingsAccounts.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using FluentValidation;
using ArtemisBankingPro.Application.Common.Validation;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Validators;

public sealed class GetSavingsAccountsPagedQueryValidator
    : AbstractValidator<GetSavingsAccountsPagedQuery> {
    private static readonly string[] AllowedStatuses = ["activa", "cancelada", "todas"];
    private static readonly string[] AllowedTypes = ["principal", "secundaria", "todas"];

    public GetSavingsAccountsPagedQueryValidator() {
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");
        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");
        RuleFor(query => query.Status)
            .Must(value => value is null || AllowedStatuses.Contains(value.ToLowerInvariant()))
            .WithMessage("El estado debe ser activa, cancelada o todas.");
        RuleFor(query => query.Type)
            .Must(value => value is null || AllowedTypes.Contains(value.ToLowerInvariant()))
            .WithMessage("El tipo debe ser principal, secundaria o todas.");
        RuleFor(query => query.Identification)
            .MaximumLength(IdentityValidationLimits.IdentificationMaxLength)
            .When(query => query.Identification is not null)
            .WithMessage(IdentityValidationLimits.IdentificationMaxLengthMessage);
    }
}

public sealed class GetAccountTransactionsQueryValidator
    : AbstractValidator<GetAccountTransactionsQuery> {
    public GetAccountTransactionsQueryValidator() {
        RuleFor(query => query.AccountNumber)
            .Matches("^[0-9]{9}$")
            .WithMessage("El número de cuenta debe contener exactamente 9 dígitos.");
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");
        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");
    }
}
