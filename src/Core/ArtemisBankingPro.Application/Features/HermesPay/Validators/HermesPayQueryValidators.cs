using ArtemisBankingPro.Application.Features.HermesPay.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.HermesPay.Validators;

public sealed class GetCommerceTransactionsQueryValidator
    : AbstractValidator<GetCommerceTransactionsQuery> {
    public GetCommerceTransactionsQueryValidator() {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");

        When(x => x.CommerceId is not null, () => {
            RuleFor(x => x.CommerceId)
                .Must(commerceId => commerceId > 0)
                .WithMessage("El comercio indicado no es válido.");
        });
    }
}
