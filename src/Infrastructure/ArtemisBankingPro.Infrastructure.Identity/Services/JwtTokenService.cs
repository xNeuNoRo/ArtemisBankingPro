using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Settings;
using ArtemisBankingPro.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ArtemisBankingPro.Infrastructure.Identity.Services;

/// <summary>
/// Emite tokens JWT firmados con los claims mínimos del contrato:
/// identificador, nombre de usuario, rol, emisión y expiración, más un jti
/// único. El comercio asociado se incluye solo para roles Comercio.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService {
    private readonly JwtSettings _settings;
    private readonly byte[] _signingKey;

    public JwtTokenService(IOptions<JwtSettings> settings) {
        _settings = settings.Value;

        if (string.IsNullOrWhiteSpace(_settings.SecretKey)) {
            throw new InvalidOperationException(
                "Security:Jwt:SecretKey no está configurada. "
                    + "Provea una clave HMAC de al menos 32 bytes en base64."
            );
        }

        _signingKey = GetKey();
        if (_signingKey.Length < 32) {
            throw new InvalidOperationException(
                "Security:Jwt:SecretKey debe contener al menos 32 bytes."
            );
        }

        if (
            string.IsNullOrWhiteSpace(_settings.Issuer)
            || string.IsNullOrWhiteSpace(_settings.Audience)
        ) {
            throw new InvalidOperationException(
                "Security:Jwt:Issuer y Security:Jwt:Audience son obligatorias."
            );
        }

        if (_settings.ExpirationMinutes < 1) {
            throw new InvalidOperationException(
                "Security:Jwt:ExpirationMinutes debe ser positivo."
            );
        }
    }

    public JwtTokenResult GenerateToken(JwtTokenRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Role);

        if (!RoleSets.Api.Contains(request.Role, StringComparer.Ordinal)) {
            throw new InvalidOperationException(
                "Solo los roles habilitados para la API pueden recibir un JWT."
            );
        }

        if (request.Role != nameof(Roles.Comercio) && request.CommerceId is not null) {
            throw new InvalidOperationException(
                "Solo un JWT de Comercio puede contener la asociación de comercio."
            );
        }

        DateTimeOffset expiresAtUtc = request.IssuedAtUtc.AddMinutes(_settings.ExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.UserId),
            new(ClaimTypes.NameIdentifier, request.UserId),
            new(ClaimTypes.Name, request.UserName),
            new(JwtRegisteredClaimNames.UniqueName, request.UserName),
            new(ClaimTypes.Role, request.Role),
            new(
                JwtRegisteredClaimNames.Iat,
                request.IssuedAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64
            ),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        if (request.CommerceId is not null) {
            claims.Add(
                new Claim(
                    CurrentUserService.CommerceIdClaim,
                    request.CommerceId.Value.ToString(CultureInfo.InvariantCulture)
                )
            );
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(_signingKey),
            SecurityAlgorithms.HmacSha256
        );

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: request.IssuedAtUtc.UtcDateTime,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: credentials
        );

        return new JwtTokenResult(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAtUtc
        );
    }

    private byte[] GetKey() {
        try {
            return Convert.FromBase64String(_settings.SecretKey!);
        }
        catch (FormatException ex) {
            throw new InvalidOperationException("Security:Jwt:SecretKey debe estar en base64.", ex);
        }
    }
}
