using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.IntegrationTests.Infrastructure;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Api;

[Collection("Api")]
public sealed class CommerceApiContractTests(ApiFactory factory) {
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
    );

    [Fact]
    public async Task CommerceCrud_UsesDocumentedContractAndPreservesStatusOnUpdate() {
        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));
        string token = await LoginTokenAsync(admin.UserName!, password);

        using HttpResponseMessage created = await SendAsync(
            HttpMethod.Post,
            "/api/commerce",
            token,
            new {
                name = "Tienda API",
                description = "Comercio HTTP",
                email = "commerce-api@example.test",
                phoneNumber = "8095551000",
                rnc = "101888001",
            },
            "commerce-create-" + Guid.NewGuid().ToString("N")
        );

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument createdBody = await ReadJsonAsync(created);
        createdBody.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal(
                "id", "name", "description", "email", "phoneNumber", "rnc", "isActive", "createdAt");
        int merchantId = createdBody.RootElement.GetProperty("id").GetInt32();
        createdBody.RootElement.GetProperty("isActive").GetBoolean().Should().BeTrue();

        using JsonDocument list = await ReadJsonAsync(
            await SendAsync(HttpMethod.Get, "/api/commerce", token, null)
        );
        list.RootElement.GetProperty("data").EnumerateArray()
            .Should().Contain(item => item.GetProperty("id").GetInt32() == merchantId);

        using JsonDocument detail = await ReadJsonAsync(
            await SendAsync(HttpMethod.Get, $"/api/commerce/{merchantId}", token, null)
        );
        detail.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal(
                "id", "name", "description", "email", "phoneNumber", "rnc", "isActive", "createdAt",
                "associatedUser");
        detail.RootElement.GetProperty("associatedUser").ValueKind.Should().Be(JsonValueKind.Null);

        using HttpResponseMessage updated = await SendAsync(
            HttpMethod.Put,
            $"/api/commerce/{merchantId}",
            token,
            new {
                name = "Tienda API Actualizada",
                description = "Actualizada",
                email = "commerce-api-updated@example.test",
                phoneNumber = "8095551001",
                rnc = "101888001",
            },
            "commerce-update-" + Guid.NewGuid().ToString("N")
        );
        updated.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using JsonDocument updatedDetail = await ReadJsonAsync(
            await SendAsync(HttpMethod.Get, $"/api/commerce/{merchantId}", token, null)
        );
        updatedDetail.RootElement.GetProperty("name").GetString().Should().Be("Tienda API Actualizada");
        updatedDetail.RootElement.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task CommerceApi_EnforcesAuthenticationValidationAndIdempotency() {
        (await _client.GetAsync("/api/commerce")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (AppUser client, _) = await CreateUserAsync(nameof(Roles.Cliente));
        string clientToken = await TokenForAsync(client, nameof(Roles.Cliente));
        using HttpResponseMessage forbidden = await SendAsync(
            HttpMethod.Get, "/api/commerce", clientToken, null);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));
        string adminToken = await LoginTokenAsync(admin.UserName!, password);
        using HttpResponseMessage invalidQuery = await SendAsync(
            HttpMethod.Get,
            "/api/commerce?page=0&pageSize=21&status=invalid",
            adminToken,
            null
        );
        invalidQuery.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage missingKey = await SendAsync(
            HttpMethod.Post,
            "/api/commerce",
            adminToken,
            new {
                name = "Sin Idempotencia",
                email = "missing-key@example.test",
                phoneNumber = "8095551002",
                rnc = "101888002",
            }
        );
        missingKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument missingKeyBody = await ReadJsonAsync(missingKey);
        missingKeyBody.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("Idempotency.MissingKey");

        using HttpResponseMessage missingStatus = await SendAsync(
            HttpMethod.Patch,
            "/api/commerce/999999/status",
            adminToken,
            new { },
            "commerce-status-missing-" + Guid.NewGuid().ToString("N")
        );
        missingStatus.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage unknown = await SendAsync(
            HttpMethod.Get,
            "/api/commerce/999999",
            adminToken,
            null
        );
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task CommerceCreate_DuplicateRncOrEmailAndIdempotencyReuseReturnConflict() {
        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));
        string token = await LoginTokenAsync(admin.UserName!, password);
        object body = new {
            name = "Comercio Único",
            email = "unique-commerce@example.test",
            phoneNumber = "8095551003",
            rnc = "101888003",
        };

        using HttpResponseMessage first = await SendAsync(
            HttpMethod.Post,
            "/api/commerce",
            token,
            body,
            "commerce-idempotency-" + Guid.NewGuid().ToString("N")
        );
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using HttpResponseMessage duplicateRnc = await SendAsync(
            HttpMethod.Post,
            "/api/commerce",
            token,
            new {
                name = "Otro Comercio",
                email = "other-commerce@example.test",
                phoneNumber = "8095551004",
                rnc = "101888003",
            },
            "commerce-duplicate-rnc-" + Guid.NewGuid().ToString("N")
        );
        duplicateRnc.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using HttpResponseMessage duplicateEmail = await SendAsync(
            HttpMethod.Post,
            "/api/commerce",
            token,
            new {
                name = "Otro Comercio 2",
                email = "UNIQUE-COMMERCE@example.test",
                phoneNumber = "8095551005",
                rnc = "101888004",
            },
            "commerce-duplicate-email-" + Guid.NewGuid().ToString("N")
        );
        duplicateEmail.StatusCode.Should().Be(HttpStatusCode.Conflict);

        string key = "commerce-reuse-" + Guid.NewGuid().ToString("N");
        using HttpResponseMessage idempotentFirst = await SendAsync(
            HttpMethod.Post,
            "/api/commerce",
            token,
            new {
                name = "Idempotente",
                email = "idempotent@example.test",
                phoneNumber = "8095551006",
                rnc = "101888005",
            },
            key
        );
        idempotentFirst.StatusCode.Should().Be(HttpStatusCode.Created);

        using HttpResponseMessage idempotentDuplicate = await SendAsync(
            HttpMethod.Post,
            "/api/commerce",
            token,
            new {
                name = "Idempotente",
                email = "idempotent@example.test",
                phoneNumber = "8095551006",
                rnc = "101888005",
            },
            key
        );
        idempotentDuplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ConcurrentCreateWithSameRnc_AllowsExactlyOneCommerce() {
        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));
        string token = await LoginTokenAsync(admin.UserName!, password);
        object body = new {
            name = "Carrera Comercio",
            email = "race-commerce@example.test",
            phoneNumber = "8095551007",
            rnc = "101888006",
        };

        HttpResponseMessage[] responses = await Task.WhenAll(
            SendAsync(HttpMethod.Post, "/api/commerce", token, body, "race-a-" + Guid.NewGuid().ToString("N")),
            SendAsync(HttpMethod.Post, "/api/commerce", token, body, "race-b-" + Guid.NewGuid().ToString("N"))
        );

        responses.Count(response => response.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
    }

    [Fact]
    public async Task DeactivateCommerce_DeactivatesAssociatedUserAndReactivationDoesNotReviveIt() {
        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));
        (AppUser commerceUser, _) = await CreateUserAsync(nameof(Roles.Comercio));
        string token = await LoginTokenAsync(admin.UserName!, password);
        int merchantId = await CreateMerchantWithUserAsync(admin.Id, commerceUser.Id);

        using HttpResponseMessage deactivated = await SendAsync(
            HttpMethod.Patch,
            $"/api/commerce/{merchantId}/status",
            token,
            new { status = false },
            "commerce-deactivate-" + Guid.NewGuid().ToString("N")
        );
        deactivated.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope()) {
            UserManager<AppUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            AppUser storedUser = (await userManager.FindByIdAsync(commerceUser.Id))!;
            storedUser.Active.Should().BeFalse();
            (await scope.ServiceProvider.GetRequiredService<BankingDbContext>().Merchants
                .Where(merchant => merchant.Id == merchantId)
                .Select(merchant => merchant.Status)
                .SingleAsync()).Should().Be(MerchantStatus.Inactive);
        }

        using HttpResponseMessage reactivated = await SendAsync(
            HttpMethod.Patch,
            $"/api/commerce/{merchantId}/status",
            token,
            new { status = true },
            "commerce-reactivate-" + Guid.NewGuid().ToString("N")
        );
        reactivated.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using AsyncServiceScope finalScope = factory.Services.CreateAsyncScope();
        AppUser stillInactive = (await finalScope.ServiceProvider
            .GetRequiredService<UserManager<AppUser>>().FindByIdAsync(commerceUser.Id))!;
        stillInactive.Active.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentStatusChanges_CommitAtMostOneDeactivationAndNeverReactivateUser() {
        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));
        (AppUser commerceUser, _) = await CreateUserAsync(nameof(Roles.Comercio));
        string token = await LoginTokenAsync(admin.UserName!, password);
        int merchantId = await CreateMerchantWithUserAsync(admin.Id, commerceUser.Id);

        HttpResponseMessage[] responses = await Task.WhenAll(
            SendAsync(
                HttpMethod.Patch,
                $"/api/commerce/{merchantId}/status",
                token,
                new { status = false },
                "commerce-race-deactivate-" + Guid.NewGuid().ToString("N")
            ),
            SendAsync(
                HttpMethod.Patch,
                $"/api/commerce/{merchantId}/status",
                token,
                new { status = true },
                "commerce-race-activate-" + Guid.NewGuid().ToString("N")
            )
        );

        int successfulChanges = responses.Count(response => response.StatusCode == HttpStatusCode.NoContent);
        successfulChanges.Should().BeInRange(1, 2);
        responses.Count(response => response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
            .Should().Be(2 - successfulChanges);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppUser storedUser = (await scope.ServiceProvider
            .GetRequiredService<UserManager<AppUser>>().FindByIdAsync(commerceUser.Id))!;
        storedUser.Active.Should().BeFalse();
        MerchantStatus finalStatus = await scope.ServiceProvider.GetRequiredService<BankingDbContext>()
            .Merchants
            .Where(merchant => merchant.Id == merchantId)
            .Select(merchant => merchant.Status)
            .SingleAsync();
        finalStatus.Should().BeOneOf(MerchantStatus.Active, MerchantStatus.Inactive);
    }

    private async Task<int> CreateMerchantWithUserAsync(string creatorId, string userId) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        Merchant merchant = Merchant.Create(
            "Comercio Asociado",
            "Asociado",
            $"associated-{Guid.NewGuid():N}@example.test",
            "8095551008",
            DigitsFromGuid(),
            creatorId,
            createdAt
        ).Value;
        merchant.AssociateUser(userId, createdAt).IsSuccess.Should().BeTrue();
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();
        return merchant.Id;
    }

    private async Task<(AppUser User, string Password)> CreateUserAsync(string role) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        string userName = $"commerce-{role.ToLowerInvariant()}-{Guid.NewGuid():N}"[..40];
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.test",
            FirstName = "Commerce",
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

    private async Task<string> LoginTokenAsync(string userName, string password) {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/account/login", new { userName, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("jwt").GetString()!;
    }

    private async Task<string> TokenForAsync(AppUser user, string role) {
        if (!RoleSets.Api.Contains(role, StringComparer.Ordinal)) {
            return TestJwtFactory.Create(user.Id, user.UserName!, role);
        }

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken(
            new JwtTokenRequest(user.Id, user.UserName!, role, null, DateTimeOffset.UtcNow)
        ).Token;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string uri,
        string token,
        object? body,
        string? idempotencyKey = null
    ) {
        using var request = new HttpRequestMessage(method, uri) {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey is not null) {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await _client.SendAsync(request);
    }

    private static string DigitsFromGuid() =>
        new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).Concat("00000000000").Take(9).ToArray());

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
