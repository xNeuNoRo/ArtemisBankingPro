using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Auth.Handlers;

/// <summary>
/// Completa el restablecimiento de contraseña: consume el token de un solo
/// uso, cambia la contraseña y reactiva la cuenta. Los mensajes siguen el
/// contrato del documento funcional.
/// </summary>
public sealed class ResetPasswordCommandHandler
    : IRequestHandler<ResetPasswordCommand, Result<Unit>> {
    private readonly IAccountTokenService _tokenService;

    public ResetPasswordCommandHandler(IAccountTokenService tokenService) {
        _tokenService = tokenService;
    }

    public async ValueTask<Result<Unit>> Handle(
        ResetPasswordCommand message,
        CancellationToken cancellationToken
    ) {
        Result<AccountTokenVerificationResult> completion =
            await _tokenService.CompletePasswordResetAsync(
            message.UserId,
            message.Token,
            message.Password,
            cancellationToken
        );

        if (completion.IsFailure) {
            return Result.Failure<Unit>(completion.Error!);
        }

        if (completion.Value == AccountTokenVerificationResult.Expired) {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Auth.ResetExpired",
                    "El enlace de restablecimiento ha expirado. "
                        + "Solicite un nuevo restablecimiento de contraseña."
                )
            );
        }

        if (completion.Value == AccountTokenVerificationResult.AlreadyUsed) {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Auth.ResetAlreadyUsed",
                    "Este enlace de restablecimiento ya fue utilizado."
                )
            );
        }

        if (completion.Value == AccountTokenVerificationResult.Invalid) {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Auth.ResetInvalid",
                    "El enlace de restablecimiento no es válido."
                )
            );
        }

        return Result.Success(Unit.Value);
    }
}
