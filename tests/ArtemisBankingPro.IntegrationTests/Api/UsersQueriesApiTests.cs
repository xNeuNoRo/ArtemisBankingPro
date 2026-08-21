using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ArtemisBankingPro.IntegrationTests.Api;

[Collection("Api")]
public sealed class UsersQueriesApiTests(ApiFactory factory) {
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
    );

    [Fact]
    public async Task GetUsers_returns_stable_admin_view_without_commerce_or_internal_fields() {
        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));
        AppUser client = (await CreateUserAsync(
            nameof(Roles.Cliente),
            createdAt: DateTimeOffset.UtcNow.AddMinutes(1)
        )).User;
        AppUser cashier = (await CreateUserAsync(
            nameof(Roles.Cajero),
            createdAt: DateTimeOffset.UtcNow.AddMinutes(2)
        )).User;
        await CreateUserAsync(
            nameof(Roles.Comercio),
            createdAt: DateTimeOffset.UtcNow.AddMinutes(3)
        );

        using HttpRequestMessage request = await AuthenticatedRequestAsync(
            HttpMethod.Get,
            "/api/users",
            admin.UserName!,
            password
        );
        using HttpResponseMessage response = await _client.SendAsync(request);
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement items = body.RootElement.GetProperty("data");
        items.EnumerateArray().Should().NotContain(item =>
            item.GetProperty("role").GetString() == nameof(Roles.Comercio)
        );

        JsonElement clientItem = items.EnumerateArray().Single(item =>
            item.GetProperty("id").GetString() == client.Id
        );
        JsonElement cashierItem = items.EnumerateArray().Single(item =>
            item.GetProperty("id").GetString() == cashier.Id
        );
        items.EnumerateArray().ToList().IndexOf(cashierItem)
            .Should().BeLessThan(items.EnumerateArray().ToList().IndexOf(clientItem));
        clientItem.TryGetProperty("createdAt", out _).Should().BeFalse();
        clientItem.TryGetProperty("passwordHash", out _).Should().BeFalse();
        clientItem.TryGetProperty("commerceId", out _).Should().BeFalse();
        clientItem.GetProperty("role").GetString().Should().Be(nameof(Roles.Cliente));
        cashierItem.GetProperty("role").GetString().Should().Be(nameof(Roles.Cajero));
    }

    [Fact]
    public async Task GetCommerceUsers_returns_associated_commerce_fields() {
        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));
        AppUser commerceUser = (await CreateUserAsync(
            nameof(Roles.Comercio),
            createdAt: DateTimeOffset.UtcNow.AddMinutes(5)
        )).User;
        (int commerceId, string commerceName) = await CreateMerchantAsync(admin.Id, commerceUser.Id);

        using HttpRequestMessage request = await AuthenticatedRequestAsync(
            HttpMethod.Get,
            "/api/users/commerce?page=1&pageSize=1",
            admin.UserName!,
            password
        );
        using HttpResponseMessage response = await _client.SendAsync(request);
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement item = body.RootElement.GetProperty("data").EnumerateArray().First(item =>
            item.GetProperty("id").GetString() == commerceUser.Id
        );
        item.GetProperty("role").GetString().Should().Be(nameof(Roles.Comercio));
        item.GetProperty("commerceId").GetInt32().Should().Be(commerceId);
        item.GetProperty("commerceName").GetString().Should().Be(commerceName);
        item.TryGetProperty("createdAt", out _).Should().BeFalse();
        item.TryGetProperty("passwordHash", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetUsers_accepts_lowercase_role_filter_and_returns_canonical_role() {
        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));
        AppUser client = (await CreateUserAsync(nameof(Roles.Cliente))).User;

        using HttpRequestMessage request = await AuthenticatedRequestAsync(
            HttpMethod.Get,
            "/api/users?role=cliente",
            admin.UserName!,
            password
        );
        using HttpResponseMessage response = await _client.SendAsync(request);
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.RootElement.GetProperty("data").EnumerateArray()
            .Should().ContainSingle(item => item.GetProperty("id").GetString() == client.Id);
        body.RootElement.GetProperty("data").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == client.Id)
            .GetProperty("role").GetString()
            .Should().Be(nameof(Roles.Cliente));
    }

    [Fact]
    public async Task GetUserById_returns_detail_and_not_found_is_problem_details() {
        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));
        AppUser client = (await CreateUserAsync(nameof(Roles.Cliente))).User;
        await CreatePrimaryAccountAsync(client.Id, admin.Id);

        using HttpRequestMessage detailRequest = await AuthenticatedRequestAsync(
            HttpMethod.Get,
            $"/api/users/{client.Id}",
            admin.UserName!,
            password
        );
        using HttpResponseMessage detailResponse = await _client.SendAsync(detailRequest);
        using JsonDocument detailBody = await ReadJsonAsync(detailResponse);

        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        detailBody.RootElement.GetProperty("id").GetString().Should().Be(client.Id);
        detailBody.RootElement.GetProperty("userName").GetString().Should().Be(client.UserName);
        detailBody.RootElement.GetProperty("mainAccount").GetProperty("accountNumber")
            .GetString().Should().HaveLength(9);
        detailBody.RootElement.GetProperty("mainAccount").GetProperty("balance")
            .GetDecimal().Should().Be(0m);
        detailBody.RootElement.TryGetProperty("passwordHash", out _).Should().BeFalse();
        detailBody.RootElement.TryGetProperty("token", out _).Should().BeFalse();

        using HttpRequestMessage missingRequest = await AuthenticatedRequestAsync(
            HttpMethod.Get,
            $"/api/users/missing-{Guid.NewGuid():N}",
            admin.UserName!,
            password
        );
        using HttpResponseMessage missingResponse = await _client.SendAsync(missingRequest);
        using JsonDocument missingBody = await ReadJsonAsync(missingResponse);

        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingBody.RootElement.GetProperty("errorCode").GetString().Should().Be("User.NotFound");
        missingResponse.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=21")]
    [InlineData("role=Comercio")]
    public async Task GetUsers_rejects_invalid_query_values(string query) {
        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));

        using HttpRequestMessage request = await AuthenticatedRequestAsync(
            HttpMethod.Get,
            $"/api/users?{query}",
            admin.UserName!,
            password
        );
        using HttpResponseMessage response = await _client.SendAsync(request);
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Validation.Failed");
        body.RootElement.GetProperty("errors").GetProperty(
            query.StartsWith("role", StringComparison.Ordinal) ? "role" : query.Split('=')[0]
        ).GetArrayLength().Should().BeGreaterThan(0);
    }

    private async Task<(AppUser User, string Password)> CreateUserAsync(
        string role,
        bool active = true,
        DateTimeOffset? createdAt = null
    ) {
        await using var scope = factory.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        string userName = $"users-query-{role.ToLowerInvariant()}-{Guid.NewGuid():N}"[..40];
        const string password = "P@ssw0rd123!";
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.test",
            FirstName = "Users",
            LastName = "Query",
            IdentityDocument = DigitsFromGuid(),
            Active = active,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return (user, password);
    }

    private async Task<(int Id, string Name)> CreateMerchantAsync(
        string creatorUserId,
        string associatedUserId
    ) {
        await using var scope = factory.Services.CreateAsyncScope();
        var merchant = ArtemisBankingPro.Domain.Merchants.Entities.Merchant.Create(
            $"Users Query Commerce {Guid.NewGuid():N}",
            "API test commerce",
            $"commerce-{Guid.NewGuid():N}@example.test",
            "8095550101",
            DigitsFromGuid(),
            creatorUserId,
            DateTimeOffset.UtcNow
        ).Value;
        merchant.AssociateUser(associatedUserId, DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();

        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();
        return (merchant.Id, merchant.Name);
    }

    private async Task CreatePrimaryAccountAsync(string ownerUserId, string createdByUserId) {
        await using var scope = factory.Services.CreateAsyncScope();
        var accountNumber = AccountNumber.Create(DigitsFromGuid()[..9]).Value;
        var account = SavingsAccount.OpenPrimary(
            ownerUserId,
            accountNumber,
            Money.Zero,
            createdByUserId,
            DateTimeOffset.UtcNow
        ).Value;

        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        context.SavingsAccounts.Add(account);
        await context.SaveChangesAsync();
    }

    private async Task<HttpRequestMessage> AuthenticatedRequestAsync(
        HttpMethod method,
        string uri,
        string userName,
        string password
    ) {
        using HttpResponseMessage login = await _client.PostAsJsonAsync(
            "/account/login",
            new { userName, password }
        );
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = await ReadJsonAsync(login);

        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            body.RootElement.GetProperty("jwt").GetString()
        );
        return request;
    }

    private static string DigitsFromGuid() =>
        new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).Concat("00000000000").Take(11).ToArray());

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
