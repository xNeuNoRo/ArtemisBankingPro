using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ArtemisBankingPro.Api.Extensions;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.IdentityModel.Tokens;

namespace ArtemisBankingPro.IntegrationTests.Api;

[Collection("Api")]
public sealed class ApiPlatformTests(ApiFactory factory) {
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
    );

    [Fact]
    public async Task Api_authorization_policies_define_the_contract_roles() {
        await using var scope = factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        AuthorizationPolicy administrator =
            (await provider.GetPolicyAsync(ApiAuthorizationPolicies.Administrator))!;
        AuthorizationPolicy administratorOrCommerce =
            (await provider.GetPolicyAsync(ApiAuthorizationPolicies.AdministratorOrCommerce))!;

        RolesAuthorizationRequirement administratorRoles = administrator.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .Should()
            .ContainSingle()
            .Which;
        RolesAuthorizationRequirement administratorOrCommerceRoles = administratorOrCommerce.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .Should()
            .ContainSingle()
            .Which;

        administratorRoles.AllowedRoles.Should().BeEquivalentTo(nameof(Roles.Administrador));
        administratorOrCommerceRoles.AllowedRoles.Should().BeEquivalentTo(
            nameof(Roles.Administrador),
            nameof(Roles.Comercio)
        );
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_contract_problem_details() {
        using HttpResponseMessage response = await _client.GetAsync("/api/users");
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ProblemContentType(response).Should().Be("application/problem+json");
        body.RootElement.GetProperty("status").GetInt32().Should().Be(401);
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Auth.Unauthenticated");
        body.RootElement.GetProperty("category").GetString().Should().Be("Unauthorized");
        body.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("instance").GetString().Should().Be("/api/users");
        response.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().Should().Be("DENY");
        response.Headers.GetValues("Referrer-Policy").Single().Should().Be("no-referrer");
        response.Headers.GetValues("Permissions-Policy").Single().Should()
            .Be("camera=(), microphone=(), geolocation=()");
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        response.Headers.Contains("Server").Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_bearer_token_returns_the_same_401_problem_contract() {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        using HttpResponseMessage response = await _client.SendAsync(request);
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ProblemContentType(response).Should().Be("application/problem+json");
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Auth.Unauthenticated");
    }

    [Fact]
    public async Task Expired_bearer_token_returns_401_without_grace_window() {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateExpiredToken());

        using HttpResponseMessage response = await _client.SendAsync(request);
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ProblemContentType(response).Should().Be("application/problem+json");
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Auth.Unauthenticated");
    }

    [Fact]
    public async Task Bearer_token_without_jti_is_rejected_even_when_signed_and_unexpired() {
        (string userName, _) = await CreateUserAsync(nameof(Roles.Administrador));
        await using var scope = factory.Services.CreateAsyncScope();
        AppUser user = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>()
            .FindByNameAsync(userName))!;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateTokenWithoutJti(user.Id)
        );

        using HttpResponseMessage response = await _client.SendAsync(request);
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Auth.Unauthenticated");
    }

    [Fact]
    public async Task Authenticated_non_admin_role_returns_problem_details_forbidden() {
        (string userName, string password) = await CreateUserAsync(nameof(Roles.Comercio));
        string token = await LoginAsync(userName, password);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await _client.SendAsync(request);
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ProblemContentType(response).Should().Be("application/problem+json");
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Auth.Forbidden");
        body.RootElement.GetProperty("status").GetInt32().Should().Be(403);
    }

    [Fact]
    public async Task Invalid_model_state_returns_problem_details_with_field_errors() {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/account/login",
            new { }
        );
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemContentType(response).Should().Be("application/problem+json");
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Validation.Failed");
        body.RootElement.GetProperty("category").GetString().Should().Be("Validation");
        body.RootElement.GetProperty("errors").GetProperty("userName").GetArrayLength().Should().BeGreaterThan(0);
        body.RootElement.GetProperty("errors").GetProperty("password").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Sensitive_model_state_errors_are_redacted() {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("password", "The value 'secret-value' is invalid.");

        var response = ArtemisBankingPro.Api.Infrastructure.ApiProblemDetailsFactory
            .FromModelState(modelState);
        var errors = (IReadOnlyDictionary<string, object?>)response.Extensions!["errors"]!;

        (response.Detail ?? string.Empty).Should().NotContain("secret-value");
        (errors["password"]?.ToString() ?? string.Empty).Should().NotContain("secret-value");
    }

    [Fact]
    public async Task Malformed_json_returns_the_dedicated_validation_problem() {
        using var content = new StringContent(
            "{\"userName\":",
            Encoding.UTF8,
            "application/json"
        );

        using HttpResponseMessage response = await _client.PostAsync("/account/login", content);
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemContentType(response).Should().Be("application/problem+json");
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Validation.MalformedJson");
        body.RootElement.GetProperty("category").GetString().Should().Be("Validation");
        body.RootElement.GetProperty("detail").GetString()
            .Should().NotContain("JsonException");
    }

    [Fact]
    public async Task Unsupported_request_content_type_is_rejected_at_the_http_boundary() {
        using var content = new StringContent("{}", Encoding.UTF8, "text/plain");
        using HttpResponseMessage response = await _client.PostAsync("/account/login", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        ProblemContentType(response).Should().Be("application/problem+json");
        using JsonDocument body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("Request.UnsupportedContentType");
    }

    [Fact]
    public async Task Unhandled_exception_returns_generic_problem_details_without_internal_data() {
        using HttpResponseMessage response = await _client.GetAsync("/__tests__/unhandled-exception");
        string responseBody = await response.Content.ReadAsStringAsync();
        using JsonDocument body = JsonDocument.Parse(responseBody);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ProblemContentType(response).Should().Be("application/problem+json");
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Internal.Unexpected");
        body.RootElement.GetProperty("detail").GetString().Should().NotContain("secret");
        responseBody.Should().NotContain("InvalidOperationException");
    }

    [Fact]
    public async Task Unsupported_method_returns_routing_problem_details() {
        using HttpResponseMessage response = await _client.GetAsync("/account/login");
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        ProblemContentType(response).Should().Be("application/problem+json");
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Route.MethodNotAllowed");
        body.RootElement.GetProperty("status").GetInt32().Should().Be(405);
    }

    [Fact]
    public async Task Payload_too_large_status_returns_contract_problem_details() {
        using HttpResponseMessage response = await _client.GetAsync("/__tests__/payload-too-large");
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        ProblemContentType(response).Should().Be("application/problem+json");
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Request.PayloadTooLarge");
        body.RootElement.GetProperty("status").GetInt32().Should().Be(413);
        body.RootElement.GetProperty("category").GetString().Should().Be("Validation");
    }

    [Fact]
    public async Task Oversized_request_body_is_rejected_before_model_binding() {
        using var content = new ByteArrayContent(new byte[1_048_577]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using HttpResponseMessage response = await _client.PostAsync("/account/login", content);
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        body.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("Request.PayloadTooLarge");
    }

    [Fact]
    public async Task Correlation_id_is_validated_echoed_and_available_in_problem_details() {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        request.Headers.Add("X-Correlation-ID", "phase1-test-123");

        using HttpResponseMessage response = await _client.SendAsync(request);
        using JsonDocument body = await ReadJsonAsync(response);

        response.Headers.GetValues("X-Correlation-ID").Single().Should().Be("phase1-test-123");
        body.RootElement.GetProperty("traceId").GetString().Should().Be("phase1-test-123");
    }

    [Fact]
    public async Task Api_login_returns_jwt_for_active_api_role() {
        (string userName, string password) = await CreateUserAsync(nameof(Roles.Administrador));

        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/account/login",
            new { userName, password }
        );
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("jwt", "expiresAtUtc", "userId", "userName", "role");
        body.RootElement.GetProperty("jwt").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);
        body.RootElement.GetProperty("userId").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("userName").GetString().Should().Be(userName);
        body.RootElement.GetProperty("role").GetString().Should().Be(nameof(Roles.Administrador));
    }

    [Fact]
    public async Task Api_login_locks_the_account_after_repeated_wrong_passwords() {
        (string userName, _) = await CreateUserAsync(nameof(Roles.Administrador));

        for (var attempt = 0; attempt < 5; attempt++) {
            using HttpResponseMessage failed = await _client.PostAsJsonAsync(
                "/account/login",
                new { userName, password = "WrongP@ssword123!" }
            );

            failed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using HttpResponseMessage locked = await _client.PostAsJsonAsync(
            "/account/login",
            new { userName, password = "P@ssw0rd123!" }
        );

        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OpenApi_and_scalar_are_available_only_to_the_test_development_surface() {
        using HttpResponseMessage openApi = await _client.GetAsync("/openapi/v1.json");
        using JsonDocument document = await ReadJsonAsync(openApi);
        using HttpResponseMessage scalar = await _client.GetAsync("/docs/");

        openApi.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer")
            .GetProperty("scheme")
            .GetString()
            .Should()
            .Be("bearer");
        document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/users")
            .GetProperty("get")
            .GetProperty("security")
            .GetArrayLength()
            .Should()
            .BeGreaterThan(0);
        document.RootElement
            .GetProperty("paths")
            .GetProperty("/pay/get-transactions/{commerceId}")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .ValueKind.Should().NotBe(JsonValueKind.Undefined);
        document.RootElement
            .GetProperty("paths")
            .GetProperty("/pay/process-payment/{commerceId}")
            .GetProperty("post")
            .GetProperty("responses")
            .GetProperty("204")
            .ValueKind.Should().NotBe(JsonValueKind.Undefined);
        document.RootElement
            .GetProperty("paths")
            .GetProperty("/__tests__/unhandled-exception")
            .GetProperty("get")
            .TryGetProperty("security", out _)
            .Should().BeFalse();
        scalar.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApi_describes_all_public_routes_security_success_and_problem_contracts() {
        using HttpResponseMessage response = await _client.GetAsync("/openapi/v1.json");
        using JsonDocument document = await ReadJsonAsync(response);
        JsonElement paths = document.RootElement.GetProperty("paths");

        var expected = new Dictionary<string, (string Method, string SuccessStatus)[]> {
            ["/account/login"] = [("post", "200")],
            ["/account/confirm"] = [("post", "204")],
            ["/account/get-reset-token"] = [("post", "204")],
            ["/account/reset-password"] = [("post", "204")],
            ["/api/users"] = [("get", "200"), ("post", "201")],
            ["/api/users/commerce"] = [("get", "200")],
            ["/api/users/{id}"] = [("get", "200"), ("put", "204")],
            ["/api/users/commerce/{commerceId}"] = [("post", "201")],
            ["/api/users/{id}/status"] = [("patch", "204")],
            ["/api/loan"] = [("get", "200"), ("post", "201")],
            ["/api/loan/{id}"] = [("get", "200")],
            ["/api/loan/{id}/rate"] = [("patch", "204")],
            ["/api/credit-card"] = [("get", "200"), ("post", "201")],
            ["/api/credit-card/{id}"] = [("get", "200")],
            ["/api/credit-card/{id}/limit"] = [("patch", "204")],
            ["/api/credit-card/{id}/cancel"] = [("patch", "204")],
            ["/api/savings-account"] = [("get", "200"), ("post", "201")],
            ["/api/savings-account/{accountNumber}/transactions"] = [("get", "200")],
            ["/api/savings-account/{accountNumber}/cancel"] = [("patch", "204")],
            ["/api/commerce"] = [("get", "200"), ("post", "201")],
            ["/api/commerce/{id}"] = [("get", "200"), ("put", "204")],
            ["/api/commerce/{id}/status"] = [("patch", "204")],
            ["/pay/get-transactions/{commerceId}"] = [("get", "200")],
            ["/pay/process-payment/{commerceId}"] = [("post", "204")],
        };

        var actualPaths = paths.EnumerateObject()
            .Where(path => !path.Name.StartsWith("/__tests__", StringComparison.Ordinal))
            .ToDictionary(path => path.Name, path => path.Value);
        actualPaths.Keys.Should().BeEquivalentTo(expected.Keys);

        foreach ((string path, (string Method, string SuccessStatus)[] operations) in expected) {
            JsonElement pathItem = actualPaths[path];
            foreach ((string method, string successStatus) in operations) {
                JsonElement operation = pathItem.GetProperty(method);
                JsonElement responses = operation.GetProperty("responses");
                responses.TryGetProperty(successStatus, out _)
                    .Should().BeTrue($"{method.ToUpperInvariant()} {path} must document {successStatus}");

                responses.TryGetProperty("400", out JsonElement badRequestResponse)
                    .Should().BeTrue($"{method.ToUpperInvariant()} {path} must document 400");
                badRequestResponse.TryGetProperty("content", out JsonElement content)
                    .Should().BeTrue($"{method.ToUpperInvariant()} {path} 400 must document content");
                content.TryGetProperty("application/problem+json", out JsonElement problemContent)
                    .Should().BeTrue($"{method.ToUpperInvariant()} {path} 400 must document application/problem+json");
                JsonElement problemSchema = problemContent
                    .GetProperty("schema");
                problemSchema.GetProperty("$ref").GetString()
                    .Should().Contain("ApiProblemDetailsContract");

                foreach (string statusCode in new[] { "401", "403", "404", "405", "409", "413", "500" }) {
                    responses.TryGetProperty(statusCode, out JsonElement boundaryResponse)
                        .Should().BeTrue($"{method.ToUpperInvariant()} {path} must document {statusCode}");
                    boundaryResponse.GetProperty("content")
                        .GetProperty("application/problem+json")
                        .GetProperty("schema")
                        .GetProperty("$ref")
                        .GetString()
                        .Should().Contain("ApiProblemDetailsContract");
                }

                if (method is "post" or "put" or "patch") {
                    responses.TryGetProperty("415", out _)
                        .Should().BeTrue($"{method.ToUpperInvariant()} {path} must document 415");
                }

                bool isAnonymous = path.StartsWith("/account/", StringComparison.Ordinal);
                if (isAnonymous) {
                    operation.TryGetProperty("security", out _).Should().BeFalse();
                }
                else {
                    operation.GetProperty("security").GetArrayLength().Should().BeGreaterThan(0);

                    if (method is "post" or "put" or "patch") {
                        JsonElement idempotencyHeader = operation
                            .GetProperty("parameters")
                            .EnumerateArray()
                            .Single(parameter =>
                                parameter.GetProperty("name").GetString() == "Idempotency-Key");
                        idempotencyHeader.GetProperty("required").GetBoolean().Should().BeTrue();
                    }
                }
            }
        }

        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement problemProperties = schemas
            .GetProperty("ApiProblemDetailsContract")
            .GetProperty("properties");
        foreach (string property in new[] { "errorCode", "category", "traceId", "errors", "resultReference" }) {
            problemProperties.TryGetProperty(property, out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details() {
        using HttpResponseMessage response = await _client.GetAsync("/api/does-not-exist");
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ProblemContentType(response).Should().Be("application/problem+json");
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Route.NotFound");
    }

    private async Task<(string UserName, string Password)> CreateUserAsync(string role) {
        string userName = $"api-{role.ToLowerInvariant()}-{Guid.NewGuid():N}"[..40];
        const string password = "P@ssw0rd123!";

        await using var scope = factory.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.test",
            FirstName = "API",
            LastName = "Test",
            IdentityDocument = DigitsFromGuid(),
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();

        return (userName, password);
    }

    private async Task<string> LoginAsync(string userName, string password) {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/account/login",
            new { userName, password }
        );
        using JsonDocument body = await ReadJsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return body.RootElement.GetProperty("jwt").GetString()!;
    }

    private static string DigitsFromGuid() =>
        new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).Concat("00000000000").Take(11).ToArray());

    private static string ProblemContentType(HttpResponseMessage response) =>
        response.Content.Headers.ContentType?.MediaType ?? string.Empty;

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static string CreateExpiredToken() {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Convert.FromBase64String(TestKeys.JwtSecretKey)),
            SecurityAlgorithms.HmacSha256
        );
        var token = new JwtSecurityToken(
            issuer: "artemis-tests",
            audience: "artemis-tests-api",
            claims: [
                new Claim(JwtRegisteredClaimNames.Sub, "expired-user"),
                new Claim(ClaimTypes.NameIdentifier, "expired-user"),
                new Claim(ClaimTypes.Role, nameof(Roles.Administrador)),
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(-5),
            signingCredentials: credentials
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateTokenWithoutJti(string userId) {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Convert.FromBase64String(TestKeys.JwtSecretKey)),
            SecurityAlgorithms.HmacSha256
        );
        var token = new JwtSecurityToken(
            issuer: "artemis-tests",
            audience: "artemis-tests-api",
            claims: [
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, nameof(Roles.Administrador)),
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
