using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.IntegrationTests.Infrastructure;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Api;

[Collection("Api")]
public sealed class CreditCardApiContractTests(ApiFactory factory) {
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
    );

    [Fact]
    public async Task Credit_card_api_uses_documented_shapes_and_direct_consumptions() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser customer, _) = await CreateUserAsync("Cliente");
        await CreatePrimaryAccountAsync(customer.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);

        using HttpResponseMessage createResponse = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/credit-card",
            token,
            new { clientId = customer.Id, creditLimit = 50_000m },
            "card-shape-" + Guid.NewGuid().ToString("N")
        );
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument created = await ReadJsonAsync(createResponse);
        created.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal(
                "id", "maskedCardNumber", "lastFourDigits", "clientId", "clientFullName",
                "creditLimit", "availableCredit", "currentDebt", "expirationDate", "status",
                "createdAt");
        created.RootElement.GetProperty("clientId").GetString().Should().Be(customer.Id);
        created.RootElement.GetProperty("status").GetString().Should().Be("Activa");
        created.RootElement.ToString().Should().NotContain("panFingerprint");
        created.RootElement.ToString().Should().NotContain("cvcDigest");

        string cardId = created.RootElement.GetProperty("id").GetString()!;
        using HttpResponseMessage detailResponse = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"/api/credit-card/{cardId}",
            token,
            null
        );
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument detail = await ReadJsonAsync(detailResponse);
        detail.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal(
                "id", "maskedCardNumber", "lastFourDigits", "clientId", "clientFullName",
                "creditLimit", "availableCredit", "currentDebt", "expirationDate", "status",
                "consumptions");
        detail.RootElement.GetProperty("consumptions").ValueKind
            .Should().Be(JsonValueKind.Array);

        using JsonDocument list = await ReadJsonAsync(
            await SendAuthenticatedAsync(HttpMethod.Get, "/api/credit-card", token, null)
        );
        list.RootElement.GetProperty("data")[0].EnumerateObject()
            .Select(property => property.Name)
            .Should().Equal(
                "id", "maskedCardNumber", "lastFourDigits", "clientId", "clientFullName",
                "creditLimit", "availableCredit", "currentDebt", "expirationDate", "status",
                "createdAt");
    }

    [Fact]
    public async Task List_defaults_to_active_but_identification_without_status_includes_history() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser customer, _) = await CreateUserAsync("Cliente");
        await CreatePrimaryAccountAsync(customer.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);

        string cancelledId = await CreateCardAsync(customer.Id, token, "card-list-cancelled");
        using HttpResponseMessage cancelResponse = await SendAuthenticatedAsync(
            HttpMethod.Patch,
            $"/api/credit-card/{cancelledId}/cancel",
            token,
            null,
            "card-list-cancel-" + Guid.NewGuid().ToString("N")
        );
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        string activeId = await CreateCardAsync(customer.Id, token, "card-list-active");

        using JsonDocument defaultList = await ReadJsonAsync(
            await SendAuthenticatedAsync(HttpMethod.Get, "/api/credit-card", token, null)
        );
        defaultList.RootElement.GetProperty("data").EnumerateArray()
            .Select(card => card.GetProperty("id").GetString())
            .Should().Contain(activeId)
            .And.NotContain(cancelledId);

        using JsonDocument historicalList = await ReadJsonAsync(
            await SendAuthenticatedAsync(
                HttpMethod.Get,
                $"/api/credit-card?identification={customer.IdentityDocument}",
                token,
                null
            )
        );
        historicalList.RootElement.GetProperty("data").GetArrayLength().Should().Be(2);
        historicalList.RootElement.GetProperty("data")[0].GetProperty("id").GetString()
            .Should().Be(activeId);
        historicalList.RootElement.GetProperty("data")[1].GetProperty("id").GetString()
            .Should().Be(cancelledId);
    }

    [Fact]
    public async Task Credit_card_api_enforces_role_and_required_request_fields() {
        using HttpResponseMessage anonymous = await _client.GetAsync("/api/credit-card");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser customer, _) = await CreateUserAsync("Cliente");
        await CreatePrimaryAccountAsync(customer.Id, admin.Id);
        string adminToken = await LoginTokenAsync(admin.UserName!, adminPassword);
        string customerToken = await TokenForAsync(customer, "Cliente");

        using HttpResponseMessage forbidden = await SendAuthenticatedAsync(
            HttpMethod.Get,
            "/api/credit-card",
            customerToken,
            null
        );
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using HttpResponseMessage missingCreateFields = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/credit-card",
            adminToken,
            new { },
            "card-missing-create-" + Guid.NewGuid().ToString("N")
        );
        missingCreateFields.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage missingLimit = await SendAuthenticatedAsync(
            HttpMethod.Patch,
            "/api/credit-card/1/limit",
            adminToken,
            new { },
            "card-missing-limit-" + Guid.NewGuid().ToString("N")
        );
        missingLimit.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage invalidStatus = await SendAuthenticatedAsync(
            HttpMethod.Get,
            "/api/credit-card?status=invalid",
            adminToken,
            null
        );
        invalidStatus.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage missingIdempotency = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/credit-card",
            adminToken,
            new { clientId = customer.Id, creditLimit = 1_000m }
        );
        missingIdempotency.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reusing_card_creation_idempotency_key_does_not_issue_twice() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser customer, _) = await CreateUserAsync("Cliente");
        await CreatePrimaryAccountAsync(customer.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);
        string key = "card-idempotency-" + Guid.NewGuid().ToString("N");
        var body = new { clientId = customer.Id, creditLimit = 12_000m };

        using HttpResponseMessage first = await SendAuthenticatedAsync(
            HttpMethod.Post, "/api/credit-card", token, body, key);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using HttpResponseMessage duplicate = await SendAuthenticatedAsync(
            HttpMethod.Post, "/api/credit-card", token, body, key);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using HttpResponseMessage reuse = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/credit-card",
            token,
            new { clientId = customer.Id, creditLimit = 13_000m },
            key
        );
        reuse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Limit_update_and_cancel_preserve_contract_and_reject_debt() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser customer, _) = await CreateUserAsync("Cliente");
        await CreatePrimaryAccountAsync(customer.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);
        string cardId = await CreateCardAsync(customer.Id, token, "card-limit");

        using HttpResponseMessage update = await SendAuthenticatedAsync(
            HttpMethod.Patch,
            $"/api/credit-card/{cardId}/limit",
            token,
            new { creditLimit = 2_000m },
            "card-limit-update-" + Guid.NewGuid().ToString("N")
        );
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AddDebtAsync(int.Parse(cardId, CultureInfo.InvariantCulture), 100m);
        using HttpResponseMessage belowDebt = await SendAuthenticatedAsync(
            HttpMethod.Patch,
            $"/api/credit-card/{cardId}/limit",
            token,
            new { creditLimit = 50m },
            "card-limit-below-debt-" + Guid.NewGuid().ToString("N")
        );
        belowDebt.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage debtCancel = await SendAuthenticatedAsync(
            HttpMethod.Patch,
            $"/api/credit-card/{cardId}/cancel",
            token,
            null,
            "card-debt-cancel-" + Guid.NewGuid().ToString("N")
        );
        debtCancel.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage detail = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"/api/credit-card/{cardId}",
            token,
            null
        );
        using JsonDocument body = await ReadJsonAsync(detail);
        body.RootElement.GetProperty("creditLimit").GetDecimal().Should().Be(2_000m);
        body.RootElement.GetProperty("status").GetString().Should().Be("Activa");
    }

    private async Task<string> CreateCardAsync(string customerId, string token, string keyPrefix) {
        using HttpResponseMessage response = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/credit-card",
            token,
            new { clientId = customerId, creditLimit = 1_000m },
            keyPrefix + "-" + Guid.NewGuid().ToString("N")
        );
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("id").GetString()!;
    }

    private async Task AddDebtAsync(int cardId, decimal amount) {
        await using var scope = factory.Services.CreateAsyncScope();
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        CreditCard card = (await context.CreditCards.SingleAsync(item => item.Id == cardId));
        card.AuthorizeCharge(Money.Create(amount).Value, DateOnly.FromDateTime(DateTime.UtcNow)).IsSuccess
            .Should().BeTrue();
        await context.SaveChangesAsync();
    }

    private async Task<(AppUser User, string Password)> CreateUserAsync(string role) {
        await using var scope = factory.Services.CreateAsyncScope();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        string userName = $"card-{role.ToLowerInvariant()}-{Guid.NewGuid():N}"[..40];
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.test",
            FirstName = "Card",
            LastName = "Contract",
            IdentityDocument = DigitsFromGuid(),
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        UserManager<AppUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        const string password = "P@ssw0rd123!";
        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return (user, password);
    }

    private async Task CreatePrimaryAccountAsync(string ownerUserId, string creatorUserId) {
        await using var scope = factory.Services.CreateAsyncScope();
        var account = SavingsAccount.OpenPrimary(
            ownerUserId,
            AccountNumber.Create(DigitsFromGuid()[..9]).Value,
            Money.Zero,
            creatorUserId,
            DateTimeOffset.UtcNow
        ).Value;
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        context.SavingsAccounts.Add(account);
        await context.SaveChangesAsync();
    }

    private async Task<string> LoginTokenAsync(string userName, string password) {
        using HttpResponseMessage login = await _client.PostAsJsonAsync(
            "/account/login", new { userName, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = await ReadJsonAsync(login);
        return body.RootElement.GetProperty("jwt").GetString()!;
    }

    private async Task<string> TokenForAsync(AppUser user, string role) {
        if (!RoleSets.Api.Contains(role, StringComparer.Ordinal)) {
            return TestJwtFactory.Create(user.Id, user.UserName!, role);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        IJwtTokenService tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return tokenService.GenerateToken(new JwtTokenRequest(
            user.Id,
            user.UserName!,
            role,
            null,
            DateTimeOffset.UtcNow
        )).Token;
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpMethod method,
        string uri,
        string token,
        object? body,
        string? idempotencyKey = null
    ) {
        var request = new HttpRequestMessage(method, uri) {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey is not null) {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return await _client.SendAsync(request);
    }

    private static string DigitsFromGuid() =>
        new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).Concat("00000000000").Take(11).ToArray());

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
