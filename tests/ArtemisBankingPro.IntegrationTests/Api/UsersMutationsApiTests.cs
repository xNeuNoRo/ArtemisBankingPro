using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Api;

[Collection("Api")]
public sealed class UsersMutationsApiTests(ApiFactory factory) {
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
    );

    [Fact]
    public async Task CreateClientUser_commits_identity_account_history_and_sends_activation_email() {
        (AppUser admin, string adminPassword) = await CreateUserAsync(nameof(Roles.Administrador));
        string userName = Unique("client");
        string email = $"{userName}@example.test";

        using HttpRequestMessage request = await AuthenticatedRequestAsync(
            HttpMethod.Post,
            "/api/users",
            admin.UserName!,
            adminPassword,
            new {
                firstName = "Ana",
                lastName = "Pérez",
                identification = DigitsFromGuid(),
                email,
                userName,
                password = "P@ssw0rd123!",
                confirmPassword = "P@ssw0rd123!",
                role = nameof(Roles.Cliente),
                initialAmount = 750m,
            },
            "users-create-" + Guid.NewGuid().ToString("N")
        );

        using HttpResponseMessage response = await _client.SendAsync(request);
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        string userId = body.RootElement.GetProperty("id").GetString()!;
        body.RootElement.GetProperty("isActive").GetBoolean().Should().BeFalse();
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("id", "userName", "email", "role", "isActive");

        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.NotNull(await userManager.FindByIdAsync(userId));

        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        SavingsAccount account = await context.SavingsAccounts.SingleAsync(
            item => item.OwnerUserId == userId
        );
        account.Balance.Amount.Should().Be(750m);
        (await context.FinancialOperations.CountAsync(operation =>
                operation.InitiatedByUserId == admin.Id
                && operation.Kind == FinancialOperationKind.InitialFunding))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateUser_reuses_idempotency_key_without_duplicate_effect_and_rejects_password_reuse() {
        (AppUser admin, string adminPassword) = await CreateUserAsync(nameof(Roles.Administrador));
        string userName = Unique("idem");
        string key = "users-idem-" + Guid.NewGuid().ToString("N");
        var payload = new {
            firstName = "Idempotent",
            lastName = "User",
            identification = DigitsFromGuid(),
            email = $"{userName}@example.test",
            userName,
            password = "P@ssw0rd123!",
            confirmPassword = "P@ssw0rd123!",
            role = nameof(Roles.Administrador),
        };

        using HttpRequestMessage first = await AuthenticatedRequestAsync(
            HttpMethod.Post, "/api/users", admin.UserName!, adminPassword, payload, key);
        using HttpResponseMessage firstResponse = await _client.SendAsync(first);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using HttpRequestMessage replay = await AuthenticatedRequestAsync(
            HttpMethod.Post, "/api/users", admin.UserName!, adminPassword, payload, key);
        using HttpResponseMessage replayResponse = await _client.SendAsync(replay);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using HttpRequestMessage changedPassword = await AuthenticatedRequestAsync(
            HttpMethod.Post,
            "/api/users",
            admin.UserName!,
            adminPassword,
            new {
                firstName = payload.firstName,
                lastName = payload.lastName,
                identification = payload.identification,
                email = payload.email,
                userName = payload.userName,
                password = "DifferentP@ss123!",
                confirmPassword = "DifferentP@ss123!",
                role = payload.role,
            },
            key
        );
        using HttpResponseMessage changedPasswordResponse = await _client.SendAsync(changedPassword);
        changedPasswordResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.NotNull(await userManager.FindByNameAsync(userName));
    }

    [Fact]
    public async Task CreateCommerceUser_associates_one_user_and_creates_primary_account() {
        (AppUser admin, string adminPassword) = await CreateUserAsync(nameof(Roles.Administrador));
        await EnsureRoleAsync(nameof(Roles.Comercio));
        int commerceId = await CreateMerchantAsync(admin.Id);
        string userName = Unique("commerce");

        using HttpRequestMessage request = await AuthenticatedRequestAsync(
            HttpMethod.Post,
            $"/api/users/commerce/{commerceId}",
            admin.UserName!,
            adminPassword,
            new {
                firstName = "Commerce",
                lastName = "User",
                identification = DigitsFromGuid(),
                email = $"{userName}@example.test",
                userName,
                password = "P@ssw0rd123!",
                confirmPassword = "P@ssw0rd123!",
                initialAmount = 300m,
            },
            "commerce-create-" + Guid.NewGuid().ToString("N")
        );

        using HttpResponseMessage response = await _client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.Created) {
            throw new Xunit.Sdk.XunitException(await response.Content.ReadAsStringAsync());
        }
        using JsonDocument body = await ReadJsonAsync(response);
        string userId = body.RootElement.GetProperty("id").GetString()!;
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("id", "userName", "email", "role", "isActive", "commerceId");

        await using var scope = factory.Services.CreateAsyncScope();
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        var merchant = await context.Merchants.SingleAsync(item => item.Id == commerceId);
        merchant.AssociatedUserId.Should().Be(userId);
        (await context.SavingsAccounts.CountAsync(item => item.OwnerUserId == userId)).Should().Be(1);
    }

    [Fact]
    public async Task CreateCommerceUser_withoutInitialAmount_returnsBadRequest() {
        (AppUser admin, string adminPassword) = await CreateUserAsync(nameof(Roles.Administrador));
        int commerceId = await CreateMerchantAsync(admin.Id);
        string userName = Unique("commerce-missing-amount");

        using HttpRequestMessage request = await AuthenticatedRequestAsync(
            HttpMethod.Post,
            $"/api/users/commerce/{commerceId}",
            admin.UserName!,
            adminPassword,
            new {
                firstName = "Commerce",
                lastName = "User",
                identification = DigitsFromGuid(),
                email = $"{userName}@example.test",
                userName,
                password = "P@ssw0rd123!",
                confirmPassword = "P@ssw0rd123!",
            },
            "commerce-missing-amount-" + Guid.NewGuid().ToString("N")
        );

        using HttpResponseMessage response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateUser_atomically_changes_profile_password_and_additional_funding() {
        (AppUser admin, string adminPassword) = await CreateUserAsync(nameof(Roles.Administrador));
        (AppUser client, _) = await CreateUserAsync(nameof(Roles.Cliente));
        await CreatePrimaryAccountAsync(client.Id, admin.Id, 100m);

        using HttpRequestMessage request = await AuthenticatedRequestAsync(
            HttpMethod.Put,
            $"/api/users/{client.Id}",
            admin.UserName!,
            adminPassword,
            new {
                firstName = "Updated",
                lastName = "Client",
                identification = client.IdentityDocument,
                email = $"{Unique("updated")}@example.test",
                userName = Unique("updated"),
                password = "N3wP@ssword!",
                confirmPassword = "N3wP@ssword!",
                additionalAmount = 50m,
            },
            "users-update-" + Guid.NewGuid().ToString("N")
        );

        using HttpResponseMessage response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        AppUser updated = (await userManager.FindByIdAsync(client.Id))!;
        updated.FirstName.Should().Be("Updated");
        (await userManager.CheckPasswordAsync(updated, "N3wP@ssword!")).Should().BeTrue();

        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        (await context.SavingsAccounts.SingleAsync(item => item.OwnerUserId == client.Id))
            .Balance.Amount.Should().Be(150m);
    }

    [Fact]
    public async Task ChangeStatus_cannot_modify_admin_itself_and_deactivates_other_user() {
        (AppUser admin, string adminPassword) = await CreateUserAsync(nameof(Roles.Administrador));
        (AppUser target, string targetPassword) = await CreateUserAsync(nameof(Roles.Cliente));

        using HttpRequestMessage self = await AuthenticatedRequestAsync(
            HttpMethod.Patch,
            $"/api/users/{admin.Id}/status",
            admin.UserName!,
            adminPassword,
            new { status = false },
            "status-self-" + Guid.NewGuid().ToString("N")
        );
        using HttpResponseMessage selfResponse = await _client.SendAsync(self);
        selfResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using HttpRequestMessage targetRequest = await AuthenticatedRequestAsync(
            HttpMethod.Patch,
            $"/api/users/{target.Id}/status",
            admin.UserName!,
            adminPassword,
            new { status = false },
            "status-target-" + Guid.NewGuid().ToString("N")
        );
        using HttpResponseMessage targetResponse = await _client.SendAsync(targetRequest);
        targetResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using HttpResponseMessage login = await _client.PostAsJsonAsync(
            "/account/login",
            new { userName = target.UserName, password = targetPassword }
        );
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Concurrent_creates_with_same_identity_have_one_success_and_one_conflict() {
        (AppUser admin, string adminPassword) = await CreateUserAsync(nameof(Roles.Administrador));
        string userName = Unique("race");
        var payload = new {
            firstName = "Race",
            lastName = "User",
            identification = DigitsFromGuid(),
            email = $"{userName}@example.test",
            userName,
            password = "P@ssw0rd123!",
            confirmPassword = "P@ssw0rd123!",
            role = nameof(Roles.Administrador),
        };
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);

        Task<HttpResponseMessage> Send(string key) {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/users") {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Idempotency-Key", key);
            return _client.SendAsync(request);
        }

        HttpResponseMessage[] responses = await Task.WhenAll(
            Send("race-a-" + Guid.NewGuid().ToString("N")),
            Send("race-b-" + Guid.NewGuid().ToString("N"))
        );
        try {
            if (responses.Any(response =>
                    response.StatusCode is not HttpStatusCode.Created
                        and not HttpStatusCode.Conflict)) {
                string details = string.Join(
                    " | ",
                    await Task.WhenAll(responses.Select(async response =>
                        $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}"))
                );
                throw new Xunit.Sdk.XunitException(details);
            }

            responses.Count(response => response.StatusCode == HttpStatusCode.Created).Should().Be(1);
            responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        }
        finally {
            foreach (HttpResponseMessage response in responses) {
                response.Dispose();
            }
        }
    }

    private async Task<(AppUser User, string Password)> CreateUserAsync(
        string role,
        bool active = true
    ) {
        await using var scope = factory.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        string userName = Unique(role.ToLowerInvariant());
        const string password = "P@ssw0rd123!";
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.test",
            FirstName = "Test",
            LastName = "User",
            IdentityDocument = DigitsFromGuid(),
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return (user, password);
    }

    private async Task EnsureRoleAsync(string role) {
        await using var scope = factory.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }
    }

    private async Task<int> CreateMerchantAsync(string creatorUserId) {
        await using var scope = factory.Services.CreateAsyncScope();
        var merchant = ArtemisBankingPro.Domain.Merchants.Entities.Merchant.Create(
            "Mutation Commerce " + Guid.NewGuid().ToString("N"),
            "API mutation test",
            $"merchant-{Guid.NewGuid():N}@example.test",
            "8095550101",
            DigitsFromGuid(),
            creatorUserId,
            DateTimeOffset.UtcNow
        ).Value;
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();
        return merchant.Id;
    }

    private async Task<string> CreatePrimaryAccountAsync(
        string ownerUserId,
        string createdByUserId,
        decimal balance
    ) {
        await using var scope = factory.Services.CreateAsyncScope();
        string number = DigitsFromGuid()[..9];
        var account = SavingsAccount.OpenPrimary(
            ownerUserId,
            AccountNumber.Create(number).Value,
            Money.Create(balance).Value,
            createdByUserId,
            DateTimeOffset.UtcNow
        ).Value;
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        context.SavingsAccounts.Add(account);
        await context.SaveChangesAsync();
        return number;
    }

    private async Task<HttpRequestMessage> AuthenticatedRequestAsync(
        HttpMethod method,
        string uri,
        string userName,
        string password,
        object body,
        string idempotencyKey
    ) {
        string token = await LoginTokenAsync(userName, password);
        var request = new HttpRequestMessage(method, uri) {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private async Task<string> LoginTokenAsync(string userName, string password) {
        using HttpResponseMessage login = await _client.PostAsJsonAsync(
            "/account/login",
            new { userName, password }
        );
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = await ReadJsonAsync(login);
        return body.RootElement.GetProperty("jwt").GetString()!;
    }

    private static string Unique(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 33)];

    private static string DigitsFromGuid() =>
        new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).Concat("00000000000").Take(11).ToArray());

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
