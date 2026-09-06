using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using System.Security.Cryptography;
using System.Text;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

/// <summary>
/// Verifica el nonce de confirmación single-use contra SQL Server real:
/// emisión con hash, consumo atómico, reuso rechazado, expiración y carrera
/// de consumo concurrente.
/// </summary>
[Collection("SqlServer")]
public sealed class ConfirmationTokenServiceTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string ActorId = "actor-confirmation";
    private const string OperationType = "ExpressTransfer";

    private static ConfirmationTokenService ResolveService(IServiceProvider services) {
        return new ConfirmationTokenService(
            services.GetRequiredService<IConfirmationTokenRepository>(),
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<IBusinessClock>()
        );
    }

    [Fact]
    public async Task IssueAndConsume_ValidNonce_ConsumesOnce() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = ResolveService(scope.ServiceProvider);

        string nonce = await service.IssueAsync(
            ActorId,
            OperationType,
            "fingerprint-1",
            TimeSpan.FromMinutes(30)
        );

        ConfirmationValidationResult result = await service.ValidateAndConsumeAsync(
            nonce,
            ActorId,
            OperationType,
            "fingerprint-1"
        );

        result.IsValid.Should().BeTrue();

        ConfirmationValidationResult reuse = await service.ValidateAndConsumeAsync(
            nonce,
            ActorId,
            OperationType,
            "fingerprint-1"
        );

        reuse.IsValid.Should().BeFalse();
        reuse.InvalidReason.Should().Be(ConfirmationTokenInvalidReason.AlreadyConsumed);
    }

    [Fact]
    public async Task Issue_PersistsHashOnly_NeverTheRawNonce() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = ResolveService(scope.ServiceProvider);

        string nonce = await service.IssueAsync(
            ActorId,
            OperationType,
            "fingerprint-hash",
            TimeSpan.FromMinutes(30)
        );

        await WithContextAsync(async context => {
            List<string> stored = await context
                .ConfirmationTokens
                .AsNoTracking()
                .Select(token => token.TokenHash)
                .ToListAsync();

            stored.Should().NotContain(nonce);
            stored.Should().OnlyContain(hash => hash.Length == 64);
        });
    }

    [Fact]
    public async Task Validate_ExpiredNonce_ReturnsExpired() {
        const string nonce = "expired-confirmation-nonce";
        await WithContextAsync(async context => {
            DateTimeOffset now = Fixture.Services
                .GetRequiredService<IBusinessClock>()
                .NowUtc;
            string hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(nonce)))
                .ToLowerInvariant();

            await context.ConfirmationTokens.AddAsync(new ConfirmationToken(
                hash,
                ActorId,
                OperationType,
                "fingerprint-expired",
                now.AddMinutes(-1),
                now.AddMinutes(-2)
            ));
            await context.SaveChangesAsync();
        });

        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = ResolveService(scope.ServiceProvider);

        ConfirmationValidationResult result = await service.ValidateAndConsumeAsync(
            nonce,
            ActorId,
            OperationType,
            "fingerprint-expired"
        );

        result.IsValid.Should().BeFalse();
        result.InvalidReason.Should().Be(ConfirmationTokenInvalidReason.Expired);
    }

    [Fact]
    public async Task Validate_WrongActor_ReturnsGenericMismatch() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = ResolveService(scope.ServiceProvider);

        string nonce = await service.IssueAsync(
            ActorId,
            OperationType,
            "fingerprint-actor",
            TimeSpan.FromMinutes(30)
        );

        ConfirmationValidationResult result = await service.ValidateAndConsumeAsync(
            nonce,
            "otro-actor",
            OperationType,
            "fingerprint-actor"
        );

        result.IsValid.Should().BeFalse();
        result.InvalidReason.Should().Be(ConfirmationTokenInvalidReason.ActorMismatch);
    }

    [Fact]
    public async Task ConcurrentConsume_ExactlyOneWinner() {
        string nonce;
        await using (var issueScope = Fixture.Services.CreateAsyncScope()) {
            var issueService = ResolveService(issueScope.ServiceProvider);
            nonce = await issueService.IssueAsync(
                ActorId,
                OperationType,
                "fingerprint-race",
                TimeSpan.FromMinutes(30)
            );
        }

        Task<ConfirmationValidationResult> ConsumeAsync() =>
            Task.Run(async () => {
                await using var scope = Fixture.Services.CreateAsyncScope();
                var service = ResolveService(scope.ServiceProvider);
                return await service.ValidateAndConsumeAsync(
                    nonce,
                    ActorId,
                    OperationType,
                    "fingerprint-race"
                );
            });

        ConfirmationValidationResult[] results = await Task.WhenAll(ConsumeAsync(), ConsumeAsync());

        results.Count(result => result.IsValid).Should().Be(1);
        results.Count(result => result.InvalidReason == ConfirmationTokenInvalidReason.AlreadyConsumed)
            .Should()
            .Be(1);
    }

}
