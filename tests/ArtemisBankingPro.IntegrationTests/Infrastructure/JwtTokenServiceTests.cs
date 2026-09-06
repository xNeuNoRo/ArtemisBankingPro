using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Infrastructure.Identity.Services;
using Microsoft.IdentityModel.Tokens;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class JwtTokenServiceTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private static DateTimeOffset NowUtc => DateTimeOffset.UtcNow;

    [Fact]
    public void GenerateToken_ContainsRequiredClaims() {
        DateTimeOffset issuedAt = NowUtc;
        using var scope = Fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        string token = service.GenerateToken(
            new JwtTokenRequest("user-1", "admin", "Administrador", null, issuedAt)
        ).Token;

        JwtSecurityToken decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        decoded.Subject.Should().Be("user-1");
        decoded.Claims.First(claim => claim.Type == ClaimTypes.NameIdentifier).Value.Should().Be("user-1");
        decoded.Claims.First(claim => claim.Type == ClaimTypes.Name).Value.Should().Be("admin");
        decoded.Claims.First(claim => claim.Type == ClaimTypes.Role).Value.Should().Be("Administrador");
        decoded.Claims.Should().Contain(claim => claim.Type == JwtRegisteredClaimNames.Iat);
        decoded.Claims.Should().Contain(claim => claim.Type == JwtRegisteredClaimNames.Jti);
        decoded.Claims.Should().NotContain(claim => claim.Type == CurrentUserService.CommerceIdClaim);
    }

    [Fact]
    public void GenerateToken_ForCommerce_IncludesCommerceIdClaim() {
        using var scope = Fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        string token = service.GenerateToken(
            new JwtTokenRequest("user-2", "comercio", "Comercio", 7, NowUtc)
        ).Token;

        JwtSecurityToken decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        decoded.Claims.First(claim => claim.Type == CurrentUserService.CommerceIdClaim).Value
            .Should().Be("7");
    }

    [Fact]
    public void GenerateToken_HasConfiguredExpiration() {
        DateTimeOffset issuedAt = NowUtc;
        using var scope = Fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        string token = service.GenerateToken(
            new JwtTokenRequest("user-1", "admin", "Administrador", null, issuedAt)
        ).Token;

        JwtSecurityToken decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        decoded.ValidFrom.Should().BeCloseTo(issuedAt.UtcDateTime, TimeSpan.FromSeconds(1));
        decoded.ValidTo.Should().BeCloseTo(issuedAt.AddMinutes(15).UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GenerateToken_SignatureValidatesAgainstConfiguredKey() {
        using var scope = Fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        string token = service.GenerateToken(
            new JwtTokenRequest("user-1", "admin", "Administrador", null, NowUtc)
        ).Token;

        var handler = new JwtSecurityTokenHandler();
        var parameters = new TokenValidationParameters {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidIssuer = "artemis-tests",
            ValidAudience = "artemis-tests-api",
            IssuerSigningKey = new SymmetricSecurityKey(
                Convert.FromBase64String(TestKeys.JwtSecretKey)
            ),
            ClockSkew = TimeSpan.Zero,
        };

        ClaimsPrincipal principal = handler.ValidateToken(token, parameters, out _);
        principal.Identity!.Name.Should().Be("admin");
        principal.IsInRole("Administrador").Should().BeTrue();
    }

    [Fact]
    public void GenerateToken_RejectsTamperedToken() {
        using var scope = Fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        string token = service.GenerateToken(
            new JwtTokenRequest("user-1", "admin", "Administrador", null, NowUtc)
        ).Token;

        string tampered = token[..^4] + "AAAA";
        var handler = new JwtSecurityTokenHandler();
        var parameters = new TokenValidationParameters {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidIssuer = "artemis-tests",
            ValidAudience = "artemis-tests-api",
            IssuerSigningKey = new SymmetricSecurityKey(
                Convert.FromBase64String(TestKeys.JwtSecretKey)
            ),
            ClockSkew = TimeSpan.Zero,
        };

        Action act = () => handler.ValidateToken(tampered, parameters, out _);
        act.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void GenerateToken_RejectsRolesOutsideApiChannel() {
        using var scope = Fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        Action act = () => service.GenerateToken(
            new JwtTokenRequest("user-1", "cliente", "Cliente", null, NowUtc)
        );

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GenerateToken_RejectsCommerceAssociationOnNonCommerceRole() {
        using var scope = Fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        Action act = () => service.GenerateToken(
            new JwtTokenRequest("user-1", "admin", "Administrador", 7, NowUtc)
        );

        act.Should().Throw<InvalidOperationException>();
    }
}
