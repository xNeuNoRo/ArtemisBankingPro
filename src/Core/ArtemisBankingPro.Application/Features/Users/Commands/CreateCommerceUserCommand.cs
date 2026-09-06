using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Users.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Users.Commands;

/// <summary>
/// Crea un usuario con rol Comercio asociado al comercio indicado (cada
/// comercio tiene un único usuario) y crea su cuenta de ahorro principal con
/// el balance inicial indicado. El usuario inicia inactivo y recibe el correo
/// de activación.
/// </summary>
public sealed record CreateCommerceUserCommand(
    int CommerceId,
    string FirstName,
    string LastName,
    string Identification,
    string Email,
    string UserName,
    string Password,
    string ConfirmPassword,
    decimal? InitialAmount = null,
    string? CallbackUrl = null
) : IRequest<Result<CreateCommerceUserResponse>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestFingerprint =>
        string.Join(
            '\u001F',
            CommerceId,
            FirstName,
            LastName,
            Identification,
            Email,
            UserName,
            "password-present",
            InitialAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            CallbackUrl ?? ""
        );
}
