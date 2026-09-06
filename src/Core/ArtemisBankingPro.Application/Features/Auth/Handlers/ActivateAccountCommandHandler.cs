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
    : IRequestHandler<ActivateAccountCommand, Result<Unit>> {
    private readonly IAccountTokenService _tokenService;

    public ActivateAccountCommandHandler(IAccountTokenService tokenService) {
        _tokenService = tokenService;
    }

    public async ValueTask<Result<Unit>> Handle(
        ActivateAccountCommand message,
        CancellationToken cancellationToken
    ) {
        Result<AccountTokenVerificationResult> completion =
            await _tokenService.CompleteActivationAsync(message.Token, cancellationToken);

        if (completion.IsFailure) {
            return Result.Failure<Unit>(completion.Error!);
        }

        if (completion.Value == AccountTokenVerificationResult.Expired) {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Account.InvalidActivationToken",
                    "El enlace de activación ha expirado. Solicite un nuevo enlace."
                )
            );
        }

        if (completion.Value == AccountTokenVerificationResult.AlreadyUsed) {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Account.InvalidActivationToken",
                    "Este enlace de activación ya fue utilizado."
                )
            );
        }

        if (completion.Value == AccountTokenVerificationResult.Invalid) {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Account.InvalidActivationToken",
                    "El enlace de activación no es válido."
                )
            );
        }

        return Result.Success(Unit.Value);
    }
}
