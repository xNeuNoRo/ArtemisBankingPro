using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Auth.Commands;

/// <summary>
/// Completa el restablecimiento de contraseña validando el token de un solo
/// uso, cambiando la contraseña y reactivando la cuenta. Endpoint público.
/// </summary>
public sealed record ResetPasswordCommand(
    string UserId,
    string Token,
    string Password,
    string ConfirmPassword
) : IRequest<Result<Unit>>;
