using ArtemisBankingPro.Application.Interfaces.Security;

namespace ArtemisBankingPro.UnitTests.Application.Interfaces;

public sealed class JwtTokenContractTests {
    [Fact]
    public void JwtTokenRequest_WithCommerceId_RetainsAllValues() {
        var issuedAt = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

        var request = new JwtTokenRequest("user-9", "comercio01", "Comercio", 7, issuedAt);

        request.UserId.Should().Be("user-9");
        request.UserName.Should().Be("comercio01");
        request.Role.Should().Be("Comercio");
        request.CommerceId.Should().Be(7);
        request.IssuedAtUtc.Should().Be(issuedAt);
    }

    [Fact]
    public void JwtTokenRequest_WithoutCommerceId_IsNull() {
        var request = new JwtTokenRequest("user-1", "admin", "Administrador", null, DateTimeOffset.UtcNow);

        request.CommerceId.Should().BeNull();
    }

    [Fact]
    public void JwtTokenResult_RetainsTokenAndExpiration() {
        var expiresAt = new DateTimeOffset(2026, 8, 7, 12, 15, 0, TimeSpan.Zero);

        var result = new JwtTokenResult("token-value", expiresAt);

        result.Token.Should().Be("token-value");
        result.ExpiresAtUtc.Should().Be(expiresAt);
    }

    [Fact]
    public void CardSecurityContract_MethodsAreDefined() {
        typeof(ICardSecurityService)
            .GetMethods()
            .Select(m => m.Name)
            .Should()
            .Contain(["ComputePanFingerprint", "ComputeCvcDigest", "VerifyCvc"]);
    }
}
