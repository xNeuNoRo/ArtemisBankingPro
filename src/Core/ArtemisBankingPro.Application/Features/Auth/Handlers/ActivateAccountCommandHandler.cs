using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Auth.Handlers;

/// <summary>
/// Activa la cuenta de un usuario consumiendo un token de activación de un
/// solo uso. Los mensajes siguen el contrato del documento funcional.
/// </summary>
public sealed class ActivateAccountCommandHandler
    : IRequestHandler<ActivateAccountCommand, Result<Unit>>
{
    private readonly IAccountTokenService _tokenService;
    private readonly IUserAccountService _userAccountService;

    public ActivateAccountCommandHandler(
        IAccountTokenService tokenService,
        IUserAccountService userAccountService
    )
    {
        _tokenService = tokenService;
        _userAccountService = userAccountService;
    }

    public async ValueTask<Result<Unit>> Handle(
        ActivateAccountCommand message,
        CancellationToken cancellationToken
    )
    {
        var verification = await _tokenService.VerifyAndConsumeByTokenAsync(
            AccountTokenType.Activation,
            message.Token,
            cancellationToken
        );

        if (verification.Result == AccountTokenVerificationResult.Expired)
        {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Auth.ActivationExpired",
                    "El enlace de activación ha expirado. Solicite un nuevo enlace."
                )
            );
        }

        if (verification.Result == AccountTokenVerificationResult.AlreadyUsed)
        {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Auth.ActivationAlreadyUsed",
                    "Este enlace de activación ya fue utilizado."
                )
            );
        }

        if (verification.Result == AccountTokenVerificationResult.Invalid)
        {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Auth.ActivationInvalid",
                    "El enlace de activación no es válido."
                )
            );
        }

        var activationResult = await _userAccountService.SetActiveAsync(
            verification.UserId!,
            isActive: true,
            cancellationToken
        );

        if (activationResult.IsFailure)
        {
            return Result.Failure<Unit>(activationResult.Error!);
        }

        return Result.Success(Unit.Value);
    }
}
