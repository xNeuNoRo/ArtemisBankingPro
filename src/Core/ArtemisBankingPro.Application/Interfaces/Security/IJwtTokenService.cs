namespace ArtemisBankingPro.Application.Interfaces.Security;

/// <summary>
/// Datos mínimos para emitir un token JWT.
/// </summary>
public sealed record JwtTokenRequest(
    string UserId,
    string UserName,
    string Role,
    int? CommerceId,
    DateTimeOffset IssuedAtUtc
);

/// <summary>
/// Resultado de la emisión de un token JWT.
/// </summary>
public sealed record JwtTokenResult(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Servicio de emisión de tokens JWT.
/// </summary>
public interface IJwtTokenService {
    JwtTokenResult GenerateToken(JwtTokenRequest request);
}
