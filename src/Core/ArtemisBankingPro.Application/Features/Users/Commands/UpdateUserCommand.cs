using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Users.Commands;

/// <summary>
/// Actualiza los datos de un usuario sin modificar su rol. Si el usuario es
/// Cliente o Comercio y se indica un monto adicional mayor que cero, el monto
/// se acredita a la cuenta de ahorro principal con una transacción CRÉDITO
/// (operación de financiamiento administrativo).
/// El administrador no puede editar su propia cuenta desde este módulo.
/// </summary>
public sealed record UpdateUserCommand(
    string UserId,
    string FirstName,
    string LastName,
    string Identification,
    string Email,
    string UserName,
    string? Password = null,
    string? ConfirmPassword = null,
    decimal? AdditionalAmount = null
) : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand, IOwnershipCheck {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestFingerprint =>
        string.Join(
            '\u001F',
            UserId,
            FirstName,
            LastName,
            Identification,
            Email,
            UserName,
            Password is null ? "password-absent" : "password-present",
            AdditionalAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""
        );
    public Task VerifyOwnershipAsync(ICurrentUserService currentUser, CancellationToken ct) {
        if (currentUser.UserId == UserId) {
            throw new ForbiddenAccessException(
                "No puede editar su propia cuenta desde este módulo."
            );
        }

        return Task.CompletedTask;
    }
}
