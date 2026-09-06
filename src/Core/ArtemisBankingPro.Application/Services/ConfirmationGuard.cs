using System.Security.Cryptography;
using System.Text;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Services;

/// <summary>
/// Application boundary for MVC confirmation nonces. API callers continue to
/// use their caller-supplied idempotency key and do not pass through this guard.
/// </summary>
public sealed class ConfirmationGuard {
    private const string OperationTypePrefix = "mvc:";

    private readonly IConfirmationTokenService _tokens;
    private readonly ICurrentUserService _currentUser;

    public ConfirmationGuard(
        IConfirmationTokenService tokens,
        ICurrentUserService currentUser
    ) {
        _tokens = tokens;
        _currentUser = currentUser;
    }

    public async Task<Result> ValidateAsync<TCommand>(
        TCommand command,
        string confirmationToken,
        CancellationToken ct = default
    ) where TCommand : IIdempotentCommand {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) {
            return Result.Failure(
                DomainError.Unauthorized(
                    "Auth.NotAuthenticated",
                    "El usuario debe estar autenticado."
                )
            );
        }

        ConfirmationValidationResult validation = await _tokens.ValidateAndConsumeAsync(
            confirmationToken,
            _currentUser.UserId,
            OperationType<TCommand>(),
            command.RequestFingerprint,
            ct
        );
        return validation.IsValid
            ? Result.Success()
            : Result.Failure(
                DomainError.Conflict(
                    "Confirmation.Invalid",
                    validation.ErrorMessage ?? "La confirmación no es válida."
                )
            );
    }

    public async Task<Result<string>> IssueAsync<TCommand>(
        TCommand command,
        TimeSpan timeToLive,
        CancellationToken ct = default
    ) where TCommand : IIdempotentCommand {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) {
            return Result.Failure<string>(
                DomainError.Unauthorized(
                    "Auth.NotAuthenticated",
                    "El usuario debe estar autenticado."
                )
            );
        }

        return Result.Success(
            await _tokens.IssueAsync(
                _currentUser.UserId,
                OperationType<TCommand>(),
                command.RequestFingerprint,
                timeToLive,
                ct
            )
        );
    }

    private static string OperationType<TCommand>() {
        string fullName = typeof(TCommand).FullName ?? typeof(TCommand).Name;
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fullName))
        );
        return OperationTypePrefix + digest[..44];
    }
}
