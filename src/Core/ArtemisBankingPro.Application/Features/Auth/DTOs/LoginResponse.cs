namespace ArtemisBankingPro.Application.Features.Auth.DTOs;

/// <summary>
/// Respuesta de login de la API: token JWT y datos mínimos del usuario.
/// </summary>
public sealed record LoginResponse(
    string Jwt,
    DateTimeOffset ExpiresAtUtc,
    string UserId,
    string UserName,
    string Role
);
