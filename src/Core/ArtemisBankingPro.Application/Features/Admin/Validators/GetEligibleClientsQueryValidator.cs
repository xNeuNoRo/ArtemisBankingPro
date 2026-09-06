using ArtemisBankingPro.Application.Features.Admin.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Admin.Validators;

public sealed class GetEligibleClientsQueryValidator
    : AbstractValidator<GetEligibleClientsQuery> {
    public GetEligibleClientsQueryValidator() {
        RuleFor(query => query.Product)
            .IsInEnum()
            .WithMessage("El tipo de producto seleccionado no es válido.");

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");

        RuleFor(query => query.Identification)
            .MaximumLength(20)
            .When(query => query.Identification is not null)
            .WithMessage("La cédula no debe exceder 20 caracteres.");
    }
}
