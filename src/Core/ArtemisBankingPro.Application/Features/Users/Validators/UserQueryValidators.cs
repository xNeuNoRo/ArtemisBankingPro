using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Users.Validators;

public sealed class GetUsersPagedQueryValidator : AbstractValidator<GetUsersPagedQuery> {
    private static readonly string[] AllowedRoles = ["administrador", "cajero", "cliente"];

    public GetUsersPagedQueryValidator() {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");

        When(x => x.Role is not null, () => {
            RuleFor(x => x.Role)
                .Must(role => AllowedRoles.Contains(role!, StringComparer.OrdinalIgnoreCase))
                .WithMessage("El rol debe ser administrador, cajero o cliente.");
        });
    }
}

public sealed class GetCommerceUsersPagedQueryValidator : AbstractValidator<GetCommerceUsersPagedQuery> {
    public GetCommerceUsersPagedQueryValidator() {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");
    }
}

public sealed class GetUserByIdQueryValidator : AbstractValidator<GetUserByIdQuery> {
    public GetUserByIdQueryValidator() {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("El identificador del usuario es requerido.")
            .MaximumLength(450);
    }
}
