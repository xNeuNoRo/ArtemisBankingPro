using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class AccountTokenServiceTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    [Fact]
    public async Task Generate_CreatesRawTokenAndPersistsOnlyHash() {
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
        string rawToken;
        await using (var scope = Fixture.Services.CreateAsyncScope()) {
            var service = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();
            rawToken = await service.GenerateAsync("user-1", AccountTokenType.Activation);
        }

        await using (var contextScope = Fixture.Services.CreateAsyncScope()) {
            var context = contextScope.ServiceProvider.GetRequiredService<IdentityContext>();
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE [Identity].[AccountTokens] SET ExpiresAtUtc = DATEADD(minute, -1, GETUTCDATE())"
            );
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
}
