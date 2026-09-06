using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Users.Commands;

/// <summary>
/// Crea un usuario (Administrador, Cajero o Cliente) inicialmente inactivo.
/// Si el rol es Cliente, crea automáticamente la cuenta de ahorro principal
/// con el monto inicial indicado (0 por defecto) y registra el financiamiento
/// inicial si el monto es mayor que cero. Envía el correo de activación.
/// </summary>
public sealed record CreateUserCommand(
    string FirstName,
    string LastName,
    string Identification,
    string Email,
    string UserName,
    string Password,
    string ConfirmPassword,
    string Role,
    decimal? InitialAmount = null,
    string? CallbackUrl = null
) : IRequest<Result<CreateUserResponse>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestFingerprint =>
        string.Join(
            '\u001F',
            FirstName,
            LastName,
            Identification,
            Email,
            UserName,
            "password-present",
            Role,
            InitialAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            CallbackUrl ?? ""
        );
}
