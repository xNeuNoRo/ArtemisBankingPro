using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ArtemisBankingPro.Infrastructure.Identity.Services;
using Microsoft.IdentityModel.Tokens;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

/// <summary>
/// Emite tokens sintéticos para probar autorización con roles que el login API
/// debe rechazar. Los flujos válidos siguen usando el issuer de producción.
/// </summary>
public static class TestJwtFactory {
    public static string Create(string userId, string userName, string role, int? commerceId = null) {
        var claims = new List<Claim> {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(
                JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64
            ),
        };

        if (commerceId is not null) {
            claims.Add(
                new Claim(
                    CurrentUserService.CommerceIdClaim,
                    commerceId.Value.ToString(CultureInfo.InvariantCulture)
                )
            );
        }

        var token = new JwtSecurityToken(
            issuer: "artemis-tests",
            audience: "artemis-tests-api",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(TestKeys.JwtSecretKey)),
                SecurityAlgorithms.HmacSha256
            )
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
