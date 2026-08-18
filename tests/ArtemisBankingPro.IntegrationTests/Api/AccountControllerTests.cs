using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ArtemisBankingPro.IntegrationTests.Api;

public sealed class AccountControllerTests : IClassFixture<ApiFactory> {
    private readonly HttpClient _client;

    public AccountControllerTests(ApiFactory factory) {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/account/login", "{\"userName\":\"\",\"password\":\"\"}")]
    [InlineData("/account/confirm", "{\"token\":\"\"}")]
    [InlineData("/account/get-reset-token", "{\"userName\":\"\"}")]
    [InlineData(
        "/account/reset-password",
        "{\"userId\":\"\",\"token\":\"\",\"password\":\"\",\"confirmPassword\":\"\"}"
    )]
    public async Task Public_account_endpoints_return_problem_details_for_invalid_input(
        string route,
        string json
    ) {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _client.PostAsync(route, content);
        string responseBody = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, responseBody);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body =
            await response.Content.ReadFromJsonAsync<JsonDocument>()
            ?? throw new InvalidOperationException("La respuesta no contiene JSON.");
        Assert.Equal("Validation.Failed", body.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Users_endpoint_requires_jwt() {
        HttpResponseMessage response = await _client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program> {
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:ArtemisDb",
            "Server=localhost,1433;Database=ArtemisBankingTests;TrustServerCertificate=True"
        );
        builder.UseSetting("Security:Jwt:SecretKey", Convert.ToBase64String(new byte[32]));
        builder.UseSetting("Security:Jwt:Issuer", "ArtemisBanking.Tests");
        builder.UseSetting("Security:Jwt:Audience", "ArtemisBanking.Tests.Api");
        builder.UseSetting("Security:Jwt:ExpirationMinutes", "15");
        builder.UseSetting("Security:AccountTokens:PepperKey", Convert.ToBase64String(new byte[32]));
        builder.UseSetting("Security:Card:FingerprintKey", Convert.ToBase64String(new byte[32]));
        builder.UseSetting("Security:Card:CvcPepperKey", Convert.ToBase64String(new byte[32]));
    }
}
