using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Users.Validators;

public sealed class GetUsersPagedQueryValidator : AbstractValidator<GetUsersPagedQuery> {
    private static readonly string[] AllowedRoles = ["Administrador", "Cajero", "Cliente"];

    public GetUsersPagedQueryValidator() {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(PageRequest.DefaultPage)
            .WithMessage("La página debe ser mayor o igual a 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}.");

        When(x => x.Role is not null, () => {
            RuleFor(x => x.Role)
                .Must(role => AllowedRoles.Contains(role!))
                .WithMessage("El rol debe ser Administrador, Cajero o Cliente.");
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
