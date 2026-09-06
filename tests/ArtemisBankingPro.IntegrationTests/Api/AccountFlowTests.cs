using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.IntegrationTests.Infrastructure;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Api;

[Collection("Api")]
public sealed class AccountFlowTests(ApiFactory factory) {
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
    );

    [Fact]
    public async Task Login_inactive_user_returns_401_without_issuing_jwt() {
        (string userName, string password, _) = await CreateUserAsync(
            nameof(Roles.Administrador),
            active: false
        );

        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/account/login",
            new { userName, password }
        );
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ProblemContentType(response).Should().Be("application/problem+json");
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Auth.Inactive");
        body.RootElement.GetProperty("detail").GetString().Should()
            .Be("Su cuenta se encuentra inactiva. Debe activar su cuenta antes de iniciar sesión.");
        body.RootElement.TryGetProperty("jwt", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Login_disallowed_api_role_returns_403() {
        (string userName, string password, _) = await CreateUserAsync(
            nameof(Roles.Cliente),
            active: true
        );

        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/account/login",
            new { userName, password }
        );
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Auth.RoleNotAllowed");
        body.RootElement.GetProperty("detail").GetString()
            .Should().Be("Este usuario no tiene permisos para acceder a la API.");
    }

    [Fact]
    public async Task Confirm_activates_user_and_consumes_token_atomically() {
        (_, _, string userId) = await CreateUserAsync(nameof(Roles.Cliente), active: false);
        string token = await GenerateTokenAsync(userId, AccountTokenType.Activation);

        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/account/confirm",
            new { token }
        );

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetUserAsync(userId)).Active.Should().BeTrue();
        (await GetTokenAsync(token, AccountTokenType.Activation)).IsUsed.Should().BeTrue();
        (await _client.PostAsJsonAsync("/account/confirm", new { token })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Confirm_concurrent_requests_allow_only_one_activation() {
        (_, _, string userId) = await CreateUserAsync(nameof(Roles.Cliente), active: false);
        string token = await GenerateTokenAsync(userId, AccountTokenType.Activation);

        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        ReferenceEquals(
            firstScope.ServiceProvider.GetRequiredService<IAccountTokenService>(),
            secondScope.ServiceProvider.GetRequiredService<IAccountTokenService>()
        ).Should().BeFalse();
        ReferenceEquals(
            firstScope.ServiceProvider.GetRequiredService<IRequestHandler<
                ActivateAccountCommand,
                Result<Unit>
            >>(),
            secondScope.ServiceProvider.GetRequiredService<IRequestHandler<
                ActivateAccountCommand,
                Result<Unit>
            >>()
        ).Should().BeFalse();

        Task<HttpResponseMessage> ConfirmAsync() =>
            _client.PostAsJsonAsync("/account/confirm", new { token });

        HttpResponseMessage[] responses = await Task.WhenAll(ConfirmAsync(), ConfirmAsync());

        string[] responseBodies = await Task.WhenAll(
            responses.Select(response => response.Content.ReadAsStringAsync())
        );
        responses.Count(response => response.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        responses.Select(response => response.StatusCode).Should()
            .Contain(
                HttpStatusCode.BadRequest,
                string.Join(" | ", responseBodies)
            );
        (await GetUserAsync(userId)).Active.Should().BeTrue();
        foreach (HttpResponseMessage response in responses) {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Confirm_expired_token_returns_contract_validation_error() {
        (_, _, string userId) = await CreateUserAsync(nameof(Roles.Cliente), active: false);
        string token = await GenerateTokenAsync(userId, AccountTokenType.Activation);
        AccountToken stored = await GetTokenAsync(token, AccountTokenType.Activation);

        await using (var scope = factory.Services.CreateAsyncScope()) {
            var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await context.AccountTokens
                .Where(token => token.Id == stored.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(token => token.CreatedAtUtc, now.AddMinutes(-2))
                    .SetProperty(token => token.ExpiresAtUtc, now.AddMinutes(-1)));
        }

        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/account/confirm",
            new { token }
        );
        using JsonDocument body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("Account.InvalidActivationToken");
        (await GetUserAsync(userId)).Active.Should().BeFalse();
    }

    [Fact]
    public async Task Reset_flow_sends_raw_token_then_changes_password_and_reactivates_user() {
        (string userName, string oldPassword, string userId) = await CreateUserAsync(
            nameof(Roles.Administrador),
            active: true
        );
        string oldSecurityStamp = (await GetUserAsync(userId)).SecurityStamp!;
        factory.EmailCapture.Clear();

        using HttpResponseMessage requestResponse = await _client.PostAsJsonAsync(
            "/account/get-reset-token",
            new { userName }
        );

        requestResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        requestResponse.Content.Headers.ContentLength.Should().Be(0);
        (await GetUserAsync(userId)).Active.Should().BeFalse();
        CapturedApiEmail email = factory.EmailCapture.Messages.Should().ContainSingle().Which;
        PasswordResetTokenModel tokenModel = Assert.IsType<PasswordResetTokenModel>(email.Model);
        string rawToken = tokenModel.Token;

        using HttpResponseMessage resetResponse = await _client.PostAsJsonAsync(
            "/account/reset-password",
            new {
                userId,
                token = rawToken,
                password = "NuevaP@ssw0rd!",
                confirmPassword = "NuevaP@ssw0rd!",
            }
        );

        resetResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        AppUser updated = await GetUserAsync(userId);
        updated.Active.Should().BeTrue();
        updated.SecurityStamp.Should().NotBe(oldSecurityStamp);
        (await CheckPasswordAsync(updated, "NuevaP@ssw0rd!")).Should().BeTrue();
        (await CheckPasswordAsync(updated, oldPassword)).Should().BeFalse();
        (await GetTokenAsync(rawToken, AccountTokenType.PasswordReset)).IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task GetResetToken_unknown_or_disallowed_user_returns_400_without_email() {
        factory.EmailCapture.Clear();

        using HttpResponseMessage unknownResponse = await _client.PostAsJsonAsync(
            "/account/get-reset-token",
            new { userName = "does-not-exist" }
        );
        unknownResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument unknownBody = await ReadJsonAsync(unknownResponse);
        unknownBody.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("Account.ResetUserNotFound");

        (string disallowedUserName, _, _) = await CreateUserAsync(nameof(Roles.Cliente), active: true);
        using HttpResponseMessage disallowedResponse = await _client.PostAsJsonAsync(
            "/account/get-reset-token",
            new { userName = disallowedUserName }
        );

        disallowedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument disallowedBody = await ReadJsonAsync(disallowedResponse);
        disallowedBody.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("Account.ResetUserNotFound");
        factory.EmailCapture.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Reset_rejects_token_bound_to_another_user_and_replay() {
        (_, string firstPassword, string firstUserId) = await CreateUserAsync(
            nameof(Roles.Administrador),
            active: true
        );
        (_, _, string secondUserId) = await CreateUserAsync(nameof(Roles.Administrador), active: true);
        string token = await GenerateTokenAsync(firstUserId, AccountTokenType.PasswordReset);

        using HttpResponseMessage wrongUserResponse = await _client.PostAsJsonAsync(
            "/account/reset-password",
            new {
                userId = secondUserId,
                token,
                password = "NuevaP@ssw0rd!",
                confirmPassword = "NuevaP@ssw0rd!",
            }
        );

        wrongUserResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetTokenAsync(token, AccountTokenType.PasswordReset)).IsUsed.Should().BeFalse();

        using HttpResponseMessage firstResetResponse = await _client.PostAsJsonAsync(
            "/account/reset-password",
            new {
                userId = firstUserId,
                token,
                password = "NuevaP@ssw0rd!",
                confirmPassword = "NuevaP@ssw0rd!",
            }
        );
        firstResetResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using HttpResponseMessage replayResponse = await _client.PostAsJsonAsync(
            "/account/reset-password",
            new {
                userId = firstUserId,
                token,
                password = "OtraP@ssw0rd!",
                confirmPassword = "OtraP@ssw0rd!",
            }
        );
        replayResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CheckPasswordAsync(await GetUserAsync(firstUserId), firstPassword)).Should().BeFalse();
    }

    private async Task<(string UserName, string Password, string UserId)> CreateUserAsync(
        string role,
        bool active
    ) {
        string userName = $"account-{role.ToLowerInvariant()}-{Guid.NewGuid():N}"[..40];
        const string password = "P@ssw0rd123!";

        await using var scope = factory.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.test",
            FirstName = "Account",
            LastName = "Test",
            IdentityDocument = DigitsFromGuid(),
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return (userName, password, user.Id);
    }

    private async Task<string> GenerateTokenAsync(string userId, AccountTokenType type) {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountTokenService>()
            .GenerateAsync(userId, type);
    }

    private async Task<AppUser> GetUserAsync(string userId) {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>()
            .FindByIdAsync(userId))!;
    }

    private async Task<AccountToken> GetTokenAsync(string rawToken, AccountTokenType type) {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();
        string pepper = TestKeys.TokenPepperKey;
        string hash = Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(
                Convert.FromBase64String(pepper),
                System.Text.Encoding.UTF8.GetBytes(rawToken)
            )
        ).ToLowerInvariant();
        return (await context.AccountTokens.SingleAsync(token =>
            token.Type == type && token.TokenHash == hash));
    }

    private async Task<bool> CheckPasswordAsync(AppUser user, string password) {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>()
            .CheckPasswordAsync(user, password);
    }

    private static string DigitsFromGuid() =>
        new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).Concat("00000000000").Take(11).ToArray());

    private static string ProblemContentType(HttpResponseMessage response) =>
        response.Content.Headers.ContentType?.MediaType ?? string.Empty;

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
