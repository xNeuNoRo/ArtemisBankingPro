using ArtemisBankingPro.Application.Features.Users.Commands;
using FluentValidation;

namespace ArtemisBankingPro.Application.Features.Users.Validators;

public sealed class ChangeUserStatusCommandValidator : AbstractValidator<ChangeUserStatusCommand>
{
    public ChangeUserStatusCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("El identificador del usuario es requerido.")
            .MaximumLength(450);
    }
}
