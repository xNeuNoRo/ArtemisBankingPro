using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using FluentValidation;
using ArtemisBankingPro.Application.Common.Validation;

namespace ArtemisBankingPro.Application.Features.CreditCard.Validator;

public sealed class GetCreditCardDetailQueryValidator : AbstractValidator<GetCreditCardDetailQuery> {
    public GetCreditCardDetailQueryValidator() {
        RuleFor(x => x.CardId)
            .GreaterThan(0)
            .WithMessage("La tarjeta es requerida.");

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");
    }
}

public sealed class GetCreditCardsPagedQueryValidator : AbstractValidator<GetCreditCardsPagedQuery> {
    private static readonly string[] AllowedStatuses = ["activa", "cancelada", "todas"];

    public GetCreditCardsPagedQueryValidator() {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");

        When(x => x.Status is not null, () => {
            RuleFor(x => x.Status)
                .Must(status => AllowedStatuses.Contains(status!))
                .WithMessage("El estado debe ser activa, cancelada o todas.");
        });

        When(x => x.Identification is not null, () => {
            RuleFor(x => x.Identification)
                .MaximumLength(IdentityValidationLimits.IdentificationMaxLength)
                .WithMessage("La cédula no debe exceder 20 caracteres.");
        });
    }
}
