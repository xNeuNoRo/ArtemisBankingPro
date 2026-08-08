using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Auth.Commands;

/// <summary>
/// Solicita el restablecimiento de contraseña: desactiva temporalmente la
/// cuenta, genera un token de un solo uso y lo envía por correo.
///
/// <paramref name="AllowedRoles"/> restringe qué roles pueden usar el flujo
/// (Administrador, Cajero, Cliente para MVC; Administrador, Comercio para API).
///
/// Si <paramref name="CallbackUrl"/> es distinto de null, el correo contiene un
/// enlace de restablecimiento (flujo MVC); si es null, contiene el token
/// directamente (flujo API).
/// </summary>
public sealed record RequestPasswordResetCommand(
    string UserName,
    IReadOnlyCollection<string> AllowedRoles,
    string? CallbackUrl = null
) : IRequest<Result<Unit>>;
