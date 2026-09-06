using ArtemisBankingPro.Application.Features.Merchants.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Merchants.Validators;

public sealed class GetMerchantsPagedQueryValidator : AbstractValidator<GetMerchantsPagedQuery> {
    private static readonly string[] AllowedStatuses = ["activo", "inactivo", "todos"];

    public GetMerchantsPagedQueryValidator() {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");

        When(x => x.Status is not null, () => {
            RuleFor(x => x.Status)
                .Must(status => AllowedStatuses.Contains(status!))
                .WithMessage("El estado solo puede tener los valores activo, inactivo o todos.");
        });
    }
}

public sealed class GetMerchantByIdQueryValidator : AbstractValidator<GetMerchantByIdQuery> {
    public GetMerchantByIdQueryValidator() {
        RuleFor(x => x.MerchantId)
            .GreaterThan(0)
            .WithMessage("El identificador del comercio es obligatorio.");
    }
}
