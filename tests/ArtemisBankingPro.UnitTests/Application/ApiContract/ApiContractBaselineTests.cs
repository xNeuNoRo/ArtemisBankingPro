using System.Reflection;
using System.Text.Json;
using ArtemisBankingPro.Application.Features.Auth.DTOs;
using ArtemisBankingPro.Application.Features.HermesPay.DTOs;
using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Enums;

namespace ArtemisBankingPro.UnitTests.Application.ApiContract;

/// <summary>
/// Executable invariants for the Phase 0 API contract baseline. Feature phases
/// add runtime endpoint coverage; these tests protect the shared contract from
/// drifting while that work is delivered incrementally.
/// </summary>
public sealed class ApiContractBaselineTests {
    private static readonly JsonSerializerOptions WebJsonOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyList<EndpointContract> Endpoints = [
        new("POST", "/account/login", ["Anonymous"], 2),
        new("POST", "/account/confirm", ["Anonymous"], 2),
        new("POST", "/account/get-reset-token", ["Anonymous"], 2),
        new("POST", "/account/reset-password", ["Anonymous"], 2),
        new("GET", "/api/users", ["Administrador"], 3),
        new("GET", "/api/users/commerce", ["Administrador"], 3),
        new("POST", "/api/users", ["Administrador"], 4),
        new("POST", "/api/users/commerce/{commerceId}", ["Administrador"], 4),
        new("PUT", "/api/users/{id}", ["Administrador"], 4),
        new("PATCH", "/api/users/{id}/status", ["Administrador"], 4),
        new("GET", "/api/users/{id}", ["Administrador"], 3),
        new("GET", "/api/loan", ["Administrador"], 5),
        new("POST", "/api/loan", ["Administrador"], 5),
        new("GET", "/api/loan/{id}", ["Administrador"], 5),
        new("PATCH", "/api/loan/{id}/rate", ["Administrador"], 5),
        new("GET", "/api/credit-card", ["Administrador"], 6),
        new("POST", "/api/credit-card", ["Administrador"], 6),
        new("GET", "/api/credit-card/{id}", ["Administrador"], 6),
        new("PATCH", "/api/credit-card/{id}/limit", ["Administrador"], 6),
        new("PATCH", "/api/credit-card/{id}/cancel", ["Administrador"], 6),
        new("GET", "/api/savings-account", ["Administrador"], 7),
        new("POST", "/api/savings-account", ["Administrador"], 7),
        new("GET", "/api/savings-account/{accountNumber}/transactions", ["Administrador"], 7),
        new("PATCH", "/api/savings-account/{accountNumber}/cancel", ["Administrador"], 7),
        new("GET", "/api/commerce", ["Administrador"], 8),
        new("GET", "/api/commerce/{id}", ["Administrador"], 8),
        new("POST", "/api/commerce", ["Administrador"], 8),
        new("PUT", "/api/commerce/{id}", ["Administrador"], 8),
        new("PATCH", "/api/commerce/{id}/status", ["Administrador"], 8),
        new("GET", "/pay/get-transactions/{commerceId}", ["Administrador", "Comercio"], 9),
        new("POST", "/pay/process-payment/{commerceId}", ["Administrador", "Comercio"], 9),
    ];

    [Fact]
    public void Functional_contract_contains_exactly_31_unique_endpoints() {
        Endpoints.Should().HaveCount(31);

        Endpoints
            .Select(endpoint => $"{endpoint.Method} {endpoint.Route}")
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Functional_contract_has_expected_phase_distribution() {
        Endpoints
            .GroupBy(endpoint => endpoint.Phase)
            .ToDictionary(group => group.Key, group => group.Count())
            .Should().Equal(
                new Dictionary<int, int> {
                    [2] = 4,
                    [3] = 3,
                    [4] = 4,
                    [5] = 4,
                    [6] = 5,
                    [7] = 4,
                    [8] = 5,
                    [9] = 2,
                }
        );
    }

    [Fact]
    public void Functional_contract_keeps_authorization_matrix_narrow() {
        Endpoints
            .Where(endpoint => endpoint.Route.StartsWith("/api/", StringComparison.Ordinal))
            .SelectMany(endpoint => endpoint.Roles)
            .Should().OnlyContain(role => role == nameof(Roles.Administrador));

        Endpoints
            .Where(endpoint => endpoint.Route.StartsWith("/pay/", StringComparison.Ordinal))
            .Should().OnlyContain(endpoint => endpoint.Roles.SequenceEqual(
                new[] { nameof(Roles.Administrador), nameof(Roles.Comercio) }
            ));

        Endpoints
            .Where(endpoint => endpoint.Route.StartsWith("/account/", StringComparison.Ordinal))
            .SelectMany(endpoint => endpoint.Roles)
            .Should().OnlyContain(role => role == "Anonymous");
    }

    [Fact]
    public void Functional_contract_does_not_introduce_speculative_version_segments() {
        Endpoints.Should().OnlyContain(endpoint => !endpoint.Route.Contains("/v1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Api_role_set_is_restricted_to_administrator_and_comercio() {
        RoleSets.Api.Should().Equal(nameof(Roles.Administrador), nameof(Roles.Comercio));
    }

    [Fact]
    public void Mvc_role_set_does_not_gain_api_only_comercio_access() {
        RoleSets.Mvc.Should().Equal(
            nameof(Roles.Administrador),
            nameof(Roles.Cajero),
            nameof(Roles.Cliente)
        );
        RoleSets.Mvc.Should().NotContain(nameof(Roles.Comercio));
    }

    [Fact]
    public void Pagination_contract_preserves_documented_limits() {
        PageRequest.DefaultPage.Should().Be(1);
        PageRequest.DefaultPageSize.Should().Be(20);
        PageRequest.MaxPageSize.Should().Be(20);

        new PageRequest().Page.Should().Be(1);
        new PageRequest().PageSize.Should().Be(20);
    }

    [Fact]
    public void Paged_merchant_response_serializes_documented_envelope_names() {
        var response = new GetMerchantsPagedResponseDto(
            1,
            20,
            0,
            0,
            []
        );

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(
            response,
            WebJsonOptions
        ));

        document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Should().Equal("page", "pageSize", "totalRecords", "totalPages", "data");
    }

    [Fact]
    public void Hermes_transaction_response_serializes_documented_fields() {
        var response = new GetCommerceTransactionsResponseDto(
            1,
            20,
            1,
            1,
            5,
            "Tienda Demo",
            [new CommerceTransactionDto(
                "100",
                new DateTimeOffset(2026, 7, 1, 15, 40, 0, TimeSpan.Zero),
                2500m,
                "1234",
                "APROBADO"
            )]
        );

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(
            response,
            WebJsonOptions
        ));
        JsonElement root = document.RootElement;

        root.EnumerateObject()
            .Select(property => property.Name)
            .Should().Equal(
                "page",
                "pageSize",
                "totalRecords",
                "totalPages",
                "commerceId",
                "commerceName",
                "data"
            );
        root.GetProperty("data")[0]
            .EnumerateObject()
            .Select(property => property.Name)
            .Should().Equal(
                "id",
                "transactionDate",
                "amount",
                "cardLastFourDigits",
                "status"
            );
    }

    [Fact]
    public void Credit_card_read_dtos_do_not_expose_sensitive_members() {
        Assembly applicationAssembly = typeof(LoginResponse).Assembly;
        IEnumerable<PropertyInfo> properties = applicationAssembly
            .GetTypes()
            .Where(type => type.Namespace?.Contains(".Features.CreditCard.DTOs", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance));

        string[] forbiddenNames = ["Pan", "CardNumber", "Cvc", "CvcDigest", "Fingerprint"];
        string[] violations = properties
            .Where(property => forbiddenNames.Any(name =>
                property.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}")
            .ToArray();

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Non_auth_read_dtos_do_not_expose_credentials_or_tokens() {
        Assembly applicationAssembly = typeof(LoginResponse).Assembly;
        IEnumerable<PropertyInfo> properties = applicationAssembly
            .GetTypes()
            .Where(type => type.Namespace?.Contains(".Features.", StringComparison.Ordinal) == true)
            .Where(type => type.Namespace?.EndsWith(".DTOs", StringComparison.Ordinal) == true)
            .Where(type => type.Namespace?.Contains(".Auth.DTOs", StringComparison.Ordinal) != true)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance));

        string[] forbiddenNames = ["Password", "PasswordHash", "ActivationToken", "ResetToken", "Cvc", "Pan"];
        string[] violations = properties
            .Where(property => forbiddenNames.Any(name =>
                property.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .Select(property => $"{property.DeclaringType?.FullName}.{property.Name}")
            .ToArray();

        violations.Should().BeEmpty();
    }

    private sealed record EndpointContract(
        string Method,
        string Route,
        IReadOnlyList<string> Roles,
        int Phase
    );
}
