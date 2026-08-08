using ArtemisBankingPro.Application.Features.Auth.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Auth.Validators;

public sealed class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand> {
    public RequestPasswordResetCommandValidator() {
        RuleFor(x => x.UserName)
            .NotEmpty()
            .WithMessage("El nombre de usuario es requerido.")
            .MaximumLength(50);

        RuleFor(x => x.AllowedRoles)
            .NotNull()
            .Must(roles => roles is { Count: > 0 })
            .WithMessage("Debe indicarse al menos un rol permitido.");

        When(x => x.CallbackUrl is not null, () => {
            RuleFor(x => x.CallbackUrl)
                .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
                .WithMessage("La URL de retorno debe ser una URL absoluta válida.");
        });
    }
}
