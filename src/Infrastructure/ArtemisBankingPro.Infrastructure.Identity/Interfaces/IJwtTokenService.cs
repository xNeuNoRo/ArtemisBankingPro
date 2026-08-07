namespace ArtemisBankingPro.Infrastructure.Identity.Interfaces;

public sealed record JwtTokenRequest(
    string UserId,
    string UserName,
    string Role,
    int? CommerceId,
    DateTimeOffset IssuedAtUtc
);

/// <summary>
/// Genera tokens JWT firmados con los claims mínimos requeridos por el
/// contrato de la API: identificador, nombre de usuario, rol, emisión y
/// expiración. Implementación interna de la infraestructura de Identity.
/// </summary>
public interface IJwtTokenService
{
    string GenerateToken(JwtTokenRequest request);
}
