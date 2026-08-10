using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using FluentValidation;

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
