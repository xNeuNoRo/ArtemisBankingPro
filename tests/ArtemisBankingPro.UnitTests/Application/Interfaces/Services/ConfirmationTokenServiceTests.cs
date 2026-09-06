using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Services;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;
using System.Data;

namespace ArtemisBankingPro.UnitTests.Application.Interfaces.Services;

public sealed class ConfirmationTokenServiceTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        ConfirmationTokenService Service,
        Mock<IConfirmationTokenRepository> Repository,
        Mock<IUnitOfWork> UnitOfWork
    );

    private static Harness CreateHarness() {
        var repository = new Mock<IConfirmationTokenRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var clock = new Mock<IBusinessClock>();
        clock.Setup(c => c.NowUtc).Returns(FixedNow);

        unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result>>>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .Returns(
                (Func<CancellationToken, Task<Result>> operation,
                 IsolationLevel _,
                 CancellationToken ct) => operation(ct)
            );

        var service = new ConfirmationTokenService(repository.Object, unitOfWork.Object, clock.Object);
        return new Harness(service, repository, unitOfWork);
    }

    [Fact]
    public async Task IssueAsync_PersistsOnlyHashAndReturnsRawToken() {
        var harness = CreateHarness();
        ConfirmationToken? persisted = null;
        harness.Repository
            .Setup(r => r.AddAsync(It.IsAny<ConfirmationToken>(), It.IsAny<CancellationToken>()))
            .Callback<ConfirmationToken, CancellationToken>((entity, _) => persisted = entity)
            .Returns(Task.CompletedTask);

        string token = await harness.Service.IssueAsync(
            "actor-1",
            "ExpressTransfer",
            "fingerprint-abc",
            TimeSpan.FromMinutes(30)
        );

        token.Should().NotBeNullOrWhiteSpace();
        Assert.NotNull(persisted);
        persisted.TokenHash.Should().NotBe(token);
        persisted.TokenHash.Should().MatchRegex("^[0-9a-f]{64}$");
        persisted.ActorId.Should().Be("actor-1");
        persisted.OperationType.Should().Be("ExpressTransfer");
        persisted.RequestFingerprint.Should().Be("fingerprint-abc");
        persisted.ExpiresAtUtc.Should().Be(FixedNow.AddMinutes(30));
        persisted.ConsumedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task IssueAsync_NonPositiveTtl_IsRejected() {
        var harness = CreateHarness();

        Func<Task> act = () => harness.Service.IssueAsync(
            "actor-1",
            "ExpressTransfer",
            "fingerprint-abc",
            TimeSpan.Zero
        );

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        harness.Repository.Verify(
            repository => repository.AddAsync(
                It.IsAny<ConfirmationToken>(),
                It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ValidToken_ConsumesOnceAndReturnsValid() {
        var harness = CreateHarness();
        ConfirmationToken? persisted = IssueAndCapture(harness);

        ConfirmationValidationResult result = await harness.Service.ValidateAndConsumeAsync(
            LastIssuedToken!,
            "actor-1",
            "ExpressTransfer",
            "fingerprint-abc"
        );

        result.IsValid.Should().BeTrue();
        persisted!.ConsumedAtUtc.Should().Be(FixedNow);
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_UnknownToken_ReturnsNotFound() {
        var harness = CreateHarness();
        harness.Repository
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConfirmationToken?)null);

        ConfirmationValidationResult result = await harness.Service.ValidateAndConsumeAsync(
            "no-existe",
            "actor-1",
            "ExpressTransfer",
            "fingerprint-abc"
        );

        result.IsValid.Should().BeFalse();
        result.InvalidReason.Should().Be(ConfirmationTokenInvalidReason.NotFound);
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_AlreadyConsumed_ReturnsAlreadyConsumed() {
        var harness = CreateHarness();
        ConfirmationToken entity = SeedToken(harness);
        entity.Consume(FixedNow.AddMinutes(-5));

        ConfirmationValidationResult result = await harness.Service.ValidateAndConsumeAsync(
            "token-abc",
            "actor-1",
            "ExpressTransfer",
            "fingerprint-abc"
        );

        result.IsValid.Should().BeFalse();
        result.InvalidReason.Should().Be(ConfirmationTokenInvalidReason.AlreadyConsumed);
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ExpiredToken_ReturnsExpired() {
        var harness = CreateHarness();
        SeedToken(harness, expired: true);

        ConfirmationValidationResult result = await harness.Service.ValidateAndConsumeAsync(
            "token-abc",
            "actor-1",
            "ExpressTransfer",
            "fingerprint-abc"
        );

        result.IsValid.Should().BeFalse();
        result.InvalidReason.Should().Be(ConfirmationTokenInvalidReason.Expired);
    }

    [Theory]
    [InlineData("actor-otro", "ExpressTransfer", "fingerprint-abc", ConfirmationTokenInvalidReason.ActorMismatch)]
    [InlineData("actor-1", "OtraOperacion", "fingerprint-abc", ConfirmationTokenInvalidReason.OperationMismatch)]
    [InlineData("actor-1", "ExpressTransfer", "fingerprint-otro", ConfirmationTokenInvalidReason.FingerprintMismatch)]
    public async Task ValidateAndConsumeAsync_MismatchedContext_ReturnsGenericMismatch(
        string actorId,
        string operationType,
        string fingerprint,
        ConfirmationTokenInvalidReason expected
    ) {
        var harness = CreateHarness();
        SeedToken(harness);

        ConfirmationValidationResult result = await harness.Service.ValidateAndConsumeAsync(
            "token-abc",
            actorId,
            operationType,
            fingerprint
        );

        result.IsValid.Should().BeFalse();
        result.InvalidReason.Should().Be(expected);
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ConcurrentConsume_ReturnsAlreadyConsumed() {
        var harness = CreateHarness();
        SeedToken(harness);

        harness.UnitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result>>>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Result.Failure(DomainError.Conflict("Concurrency.Conflict", "conflicto"))
            );

        ConfirmationValidationResult result = await harness.Service.ValidateAndConsumeAsync(
            "token-abc",
            "actor-1",
            "ExpressTransfer",
            "fingerprint-abc"
        );

        result.IsValid.Should().BeFalse();
        result.InvalidReason.Should().Be(ConfirmationTokenInvalidReason.AlreadyConsumed);
    }

    private static string? LastIssuedToken;

    private static ConfirmationToken? IssueAndCapture(Harness harness) {
        ConfirmationToken? persisted = null;
        harness.Repository
            .Setup(r => r.AddAsync(It.IsAny<ConfirmationToken>(), It.IsAny<CancellationToken>()))
            .Callback<ConfirmationToken, CancellationToken>((entity, _) => persisted = entity)
            .Returns(Task.CompletedTask);

        // El repositorio devuelve la entidad emitida cuando se busca por su hash real.
        harness.Repository
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (string hash, CancellationToken _) =>
                    persisted is not null && persisted.TokenHash == hash ? persisted : null
            );

        LastIssuedToken = harness.Service
            .IssueAsync("actor-1", "ExpressTransfer", "fingerprint-abc", TimeSpan.FromMinutes(30))
            .GetAwaiter()
            .GetResult();
        return persisted;
    }

    private static ConfirmationToken SeedToken(Harness harness, bool expired = false) {
        var entity = new ConfirmationToken(
            "hash-fijo",
            "actor-1",
            "ExpressTransfer",
            "fingerprint-abc",
            expired ? FixedNow.AddMinutes(-1) : FixedNow.AddMinutes(30),
            FixedNow
        );

        // Devuelve la entidad para cualquier hash: el servicio valida la entidad
        // encontrada sin comparar el hash de entrada (eso lo hace la BD real).
        harness.Repository
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        return entity;
    }
}
