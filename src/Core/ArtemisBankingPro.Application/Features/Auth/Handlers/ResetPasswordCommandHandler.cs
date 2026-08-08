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
    private readonly IUserAccountService _userAccountService;

    public ResetPasswordCommandHandler(
        IAccountTokenService tokenService,
        IUserAccountService userAccountService
    ) {
        _tokenService = tokenService;
        _userAccountService = userAccountService;
    }

    public async ValueTask<Result<Unit>> Handle(
        ResetPasswordCommand message,
        CancellationToken cancellationToken
    ) {
        var verification = await _tokenService.VerifyAndConsumeAsync(
            message.UserId,
            AccountTokenType.PasswordReset,
            message.Token,
            cancellationToken
        );

        if (verification == AccountTokenVerificationResult.Expired) {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Auth.ResetExpired",
                    "El enlace de restablecimiento ha expirado. "
                        + "Solicite un nuevo restablecimiento de contraseña."
                )
            );
        }

        if (verification == AccountTokenVerificationResult.AlreadyUsed) {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Auth.ResetAlreadyUsed",
                    "Este enlace de restablecimiento ya fue utilizado."
                )
            );
        }

        if (verification == AccountTokenVerificationResult.Invalid) {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Auth.ResetInvalid",
                    "El enlace de restablecimiento no es válido."
                )
            );
        }

        var passwordResult = await _userAccountService.ChangePasswordAsync(
            message.UserId,
            message.Password,
            cancellationToken
        );
        if (passwordResult.IsFailure) {
            return Result.Failure<Unit>(passwordResult.Error!);
        }

        var reactivateResult = await _userAccountService.SetActiveAsync(
            message.UserId,
            isActive: true,
            cancellationToken
        );

        if (reactivateResult.IsFailure) {
            return Result.Failure<Unit>(reactivateResult.Error!);
        }

        return Result.Success(Unit.Value);
    }
}
