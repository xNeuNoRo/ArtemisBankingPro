using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class AccountTokenServiceTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    [Fact]
    public async Task Generate_CreatesRawTokenAndPersistsOnlyHash() {
        await EnsureUserAsync("user-1");
        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();
        var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();

        string rawToken = await service.GenerateAsync("user-1", AccountTokenType.Activation);

        rawToken.Should().NotBeNullOrWhiteSpace();
        rawToken.Should().NotContain("=");

        AccountToken stored = await context.AccountTokens.SingleAsync();
        stored.UserId.Should().Be("user-1");
        stored.Type.Should().Be(AccountTokenType.Activation);
        stored.TokenHash.Should().NotBe(rawToken);
        stored.TokenHash.Length.Should().Be(64);
        stored.IsUsed.Should().BeFalse();
        stored.ExpiresAtUtc.Should().BeAfter(stored.CreatedAtUtc);
    }

    [Fact]
    public async Task Generate_InvalidatesPreviousUnusedTokensOfSameType() {
        await EnsureUserAsync("user-1");
        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();

        string first = await service.GenerateAsync("user-1", AccountTokenType.PasswordReset);
        string second = await service.GenerateAsync("user-1", AccountTokenType.PasswordReset);

        first.Should().NotBe(second);

        var result = await service.VerifyAndConsumeAsync(
            "user-1",
            AccountTokenType.PasswordReset,
            first
        );
        result.Should().Be(AccountTokenVerificationResult.AlreadyUsed);

        var secondResult = await service.VerifyAndConsumeAsync(
            "user-1",
            AccountTokenType.PasswordReset,
            second
        );
        secondResult.Should().Be(AccountTokenVerificationResult.Valid);
    }

    [Fact]
    public async Task VerifyAndConsume_ValidToken_ConsumesItOnce() {
        await EnsureUserAsync("user-1");
        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();

        string rawToken = await service.GenerateAsync("user-1", AccountTokenType.Activation);

        var first = await service.VerifyAndConsumeAsync("user-1", AccountTokenType.Activation, rawToken);
        first.Should().Be(AccountTokenVerificationResult.Valid);

        var second = await service.VerifyAndConsumeAsync("user-1", AccountTokenType.Activation, rawToken);
        second.Should().Be(AccountTokenVerificationResult.AlreadyUsed);
    }

    [Fact]
    public async Task VerifyAndConsume_ExpiredToken_ReturnsExpired() {
        await EnsureUserAsync("user-1");
        string rawToken;
        await using (var scope = Fixture.Services.CreateAsyncScope()) {
            var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();
            rawToken = await service.GenerateAsync("user-1", AccountTokenType.Activation);
        }

        await using (var contextScope = Fixture.Services.CreateAsyncScope()) {
            var context = contextScope.ServiceProvider.GetRequiredService<IdentityContext>();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await context.AccountTokens.ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.CreatedAtUtc, now.AddMinutes(-2))
                .SetProperty(token => token.ExpiresAtUtc, now.AddMinutes(-1)));
        }

        await using (var scope = Fixture.Services.CreateAsyncScope()) {
            var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();
            var result = await service.VerifyAndConsumeAsync(
                "user-1",
                AccountTokenType.Activation,
                rawToken
            );
            result.Should().Be(AccountTokenVerificationResult.Expired);
        }
    }

    [Fact]
    public async Task VerifyAndConsume_WrongUserOrPurposeOrToken_ReturnsInvalid() {
        await EnsureUserAsync("user-1");
        await EnsureUserAsync("user-2");
        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();

        string rawToken = await service.GenerateAsync("user-1", AccountTokenType.Activation);

        (await service.VerifyAndConsumeAsync("user-2", AccountTokenType.Activation, rawToken))
            .Should().Be(AccountTokenVerificationResult.Invalid);
        (await service.VerifyAndConsumeAsync("user-1", AccountTokenType.PasswordReset, rawToken))
            .Should().Be(AccountTokenVerificationResult.Invalid);
        (await service.VerifyAndConsumeAsync("user-1", AccountTokenType.Activation, "wrong-token"))
            .Should().Be(AccountTokenVerificationResult.Invalid);
        (await service.VerifyAndConsumeAsync("user-1", AccountTokenType.Activation, string.Empty))
            .Should().Be(AccountTokenVerificationResult.Invalid);
    }

    [Fact]
    public async Task VerifyAndConsume_ConcurrentRequests_ExactlyOneSucceeds() {
        await EnsureUserAsync("user-1");
        string rawToken;
        await using (var scope = Fixture.Services.CreateAsyncScope()) {
            var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();
            rawToken = await service.GenerateAsync("user-1", AccountTokenType.Activation);
        }

        Task<AccountTokenVerificationResult> VerifyAsync() =>
            Task.Run(async () => {
                await using var scope = Fixture.Services.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();
                return await service.VerifyAndConsumeAsync(
                    "user-1",
                    AccountTokenType.Activation,
                    rawToken
                );
            });

        AccountTokenVerificationResult[] results = await Task.WhenAll(
            VerifyAsync(),
            VerifyAsync()
        );

        results.Count(result => result == AccountTokenVerificationResult.Valid).Should().Be(1);
        results.Count(result => result == AccountTokenVerificationResult.AlreadyUsed).Should().Be(1);
    }

    [Fact]
    public async Task Generate_ConcurrentRequests_LeaveOnlyOneActiveToken() {
        await EnsureUserAsync("user-1");

        Task<string> GenerateAsync() => Task.Run(async () => {
            await using var scope = Fixture.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();
            return await service.GenerateAsync("user-1", AccountTokenType.Activation);
        });

        string[] tokens = await Task.WhenAll(GenerateAsync(), GenerateAsync());

        await using var verificationScope = Fixture.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<IdentityContext>();
        (await context.AccountTokens.CountAsync(item => item.UserId == "user-1" && item.UsedAtUtc == null))
            .Should().Be(1);

        async Task<AccountTokenVerificationResult> VerifyAsync(string token) {
            await using var scope = Fixture.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();
            return await service.VerifyAndConsumeAsync("user-1", AccountTokenType.Activation, token);
        }

        var results = await Task.WhenAll(VerifyAsync(tokens[0]), VerifyAsync(tokens[1]));
        results.Count(result => result == AccountTokenVerificationResult.Valid).Should().Be(1);
    }

    [Fact]
    public async Task GeneratePasswordReset_AppliesCooldownAndAtomicallyDeactivatesUser() {
        await EnsureUserAsync("user-1", active: true);
        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        Result<PasswordResetTokenResult> first = await service.GeneratePasswordResetAsync(
            "user-1",
            ["Cliente"]
        );
        Result<PasswordResetTokenResult> second = await service.GeneratePasswordResetAsync(
            "user-1",
            ["Cliente"]
        );

        first.IsSuccess.Should().BeTrue();
        second.IsFailure.Should().BeTrue();
        second.Error!.Code.Should().Be("Auth.ResetCooldown");
        (await manager.FindByIdAsync("user-1"))!.Active.Should().BeFalse();
    }

    [Fact]
    public async Task GeneratePasswordReset_EnforcesWindowLimitAfterCooldown() {
        await EnsureUserAsync("user-1", active: true);
        await using ServiceProvider provider = Fixture.BuildProvider(
            extraConfiguration: new Dictionary<string, string?> {
                ["Security:AccountTokens:ResetRequestCooldownSeconds"] = "1",
                ["Security:AccountTokens:ResetRequestWindowMinutes"] = "15",
                ["Security:AccountTokens:MaxResetRequestsPerWindow"] = "2",
            }
        );

        async Task<Result<PasswordResetTokenResult>> GenerateResetAsync() {
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();
            return await service.GeneratePasswordResetAsync("user-1", ["Cliente"]);
        }

        (await GenerateResetAsync()).IsSuccess.Should().BeTrue();
        await AgeResetTokensAsync(provider);
        (await GenerateResetAsync()).IsSuccess.Should().BeTrue();
        await AgeResetTokensAsync(provider);

        Result<PasswordResetTokenResult> third = await GenerateResetAsync();

        third.IsFailure.Should().BeTrue();
        third.Error!.Code.Should().Be("Auth.ResetRateLimited");
    }

    private static async Task AgeResetTokensAsync(ServiceProvider provider) {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();
        await context.AccountTokens.ExecuteUpdateAsync(setters => setters
            .SetProperty(token => token.CreatedAtUtc, DateTimeOffset.UtcNow.AddSeconds(-2)));
    }

    private async Task EnsureUserAsync(string userId, bool active = false) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        UserManager<AppUser> manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser {
            Id = userId,
            UserName = userId,
            Email = $"{userId}@example.com",
            FirstName = "Token",
            LastName = "Test",
            IdentityDocument = $"TOKEN{userId[^1]}",
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        (await manager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(nameof(Roles.Cliente))) {
            (await roleManager.CreateAsync(new IdentityRole(nameof(Roles.Cliente)))).Succeeded
                .Should().BeTrue();
        }
        (await manager.AddToRoleAsync(user, nameof(Roles.Cliente))).Succeeded.Should().BeTrue();
    }
}
