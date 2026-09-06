using ArtemisBankingPro.Application.Common.Behaviors;
using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Cryptography;
using System.Text;

namespace ArtemisBankingPro.UnitTests.Application.Common.Behaviors;

public sealed record IdempotentRequest(string Payload) : IRequest<Result<Unit>>, IIdempotentCommand {
    public string IdempotencyKey => $"test-key-{Payload}";
    public string RequestFingerprint => Payload;
}

public sealed record SystemIdempotentRequest(string Payload)
    : IRequest<Result<Unit>>, IIdempotentCommand {
    public string IdempotencyKey => $"system-key-{Payload}";
    public string RequestFingerprint => Payload;
    public string? IdempotencyActorId => "system:test";
}

public sealed record MissingKeyRequest(string Payload) : IRequest<Result<Unit>>, IIdempotentCommand {
    public string IdempotencyKey => string.Empty;
    public string RequestFingerprint => Payload;
}

public sealed class IdempotencyBehaviorTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static Mock<IBusinessClock> Clock() {
        var clock = new Mock<IBusinessClock>();
        clock.SetupGet(c => c.NowUtc).Returns(FixedNow);
        return clock;
    }

    private static Mock<ICurrentUserService> User(string userId = "actor-1") {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.UserId).Returns(userId);
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        return user;
    }

    private static Mock<IUnitOfWork> UnitOfWork() {
        var uow = new Mock<IUnitOfWork>();
        uow
            .Setup(u =>
                u.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result>>>(),
                    It.IsAny<System.Data.IsolationLevel>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (Func<CancellationToken, Task<Result>> operation,
                    System.Data.IsolationLevel _,
                    CancellationToken ct) => operation(ct)
            );
        return uow;
    }

    private static IdempotencyBehavior<IdempotentRequest, Result<Unit>> Behavior(
        IIdempotencyRecordRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUserService user,
        IBusinessClock clock
    ) =>
        new(
            repository,
            unitOfWork,
            user,
            clock,
            NullLogger<IdempotencyBehavior<IdempotentRequest, Result<Unit>>>.Instance
        );

    private static MessageHandlerDelegate<IdempotentRequest, Result<Unit>> SuccessDelegate() =>
        (_, _) => ValueTask.FromResult(Result.Success(Unit.Value));

    private static MessageHandlerDelegate<IdempotentRequest, Result<Unit>> FailureDelegate() =>
        (_, _) => ValueTask.FromResult(
            Result.Failure<Unit>(DomainError.Declined("Test.Declined", "Rechazado."))
        );

    private static MessageHandlerDelegate<IdempotentRequest, Result<Unit>> ThrowingDelegate() =>
        (_, _) => throw new InvalidOperationException("Handler failed.");

    private static MessageHandlerDelegate<IdempotentRequest, Result<Unit>> CancellingDelegate() =>
        (_, ct) => throw new OperationCanceledException(ct);

    private static Mock<IIdempotencyRecordRepository> EmptyRepository() {
        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);
        return repository;
    }

    [Fact]
    public async Task Handle_FirstExecution_ClaimsAndCompletesRecord() {
        var repository = EmptyRepository();

        var behavior = Behavior(repository.Object, UnitOfWork().Object, User().Object, Clock().Object);

        Result<Unit> result = await behavior.Handle(
            new IdempotentRequest("abc"),
            SuccessDelegate(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        repository.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.Update(It.IsAny<IdempotencyRecord>()), Times.Once);
        repository.Verify(r => r.Delete(It.IsAny<IdempotencyRecord>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateCompletedKey_ThrowsIdempotencyConflict() {
        var completed = new IdempotencyRecord(
            "test-key-abc",
            "actor-1",
            "IdempotentRequest",
            Fingerprint("abc"),
            FixedNow
        );
        completed.Complete("test-key-abc", FixedNow);

        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync("test-key-abc", "actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(completed);

        var behavior = Behavior(repository.Object, UnitOfWork().Object, User().Object, Clock().Object);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() => behavior
            .Handle(new IdempotentRequest("abc"), SuccessDelegate(), CancellationToken.None)
            .AsTask());
        repository.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SameKeyDifferentOperationType_ThrowsIdempotencyConflict() {
        var existing = new IdempotencyRecord(
            "test-key-abc",
            "actor-1",
            "AnotherOperation",
            Fingerprint("abc"),
            FixedNow
        );
        existing.Complete("test-key-abc", FixedNow);

        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync("test-key-abc", "actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var behavior = Behavior(repository.Object, UnitOfWork().Object, User().Object, Clock().Object);

        var exception = await Assert.ThrowsAsync<IdempotencyConflictException>(() => behavior
            .Handle(new IdempotentRequest("abc"), SuccessDelegate(), CancellationToken.None)
            .AsTask());
        exception.Message.Should().Contain("tipo de operación");
        repository.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateRejectedKey_ThrowsIdempotencyConflict() {
        var rejected = new IdempotencyRecord(
            "test-key-abc",
            "actor-1",
            "IdempotentRequest",
            Fingerprint("abc"),
            FixedNow
        );
        rejected.Reject("Test.Declined", FixedNow);

        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync("test-key-abc", "actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rejected);

        var behavior = Behavior(repository.Object, UnitOfWork().Object, User().Object, Clock().Object);

        Func<Task> act = () => behavior
            .Handle(new IdempotentRequest("abc"), SuccessDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<IdempotencyConflictException>();
        repository.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SameKeyDifferentFingerprint_ThrowsIdempotencyConflict() {
        var existing = new IdempotencyRecord(
            "test-key-abc",
            "actor-1",
            "IdempotentRequest",
            Fingerprint("original"),
            FixedNow
        );

        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync("test-key-abc", "actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var behavior = Behavior(repository.Object, UnitOfWork().Object, User().Object, Clock().Object);

        Func<Task> act = () => behavior
            .Handle(new IdempotentRequest("abc"), SuccessDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<IdempotencyConflictException>();
    }

    [Fact]
    public async Task Handle_InProgressKey_ThrowsIdempotencyConflict() {
        var inProgress = new IdempotencyRecord(
            "test-key-abc",
            "actor-1",
            "IdempotentRequest",
            Fingerprint("abc"),
            FixedNow
        );

        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync("test-key-abc", "actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(inProgress);

        var behavior = Behavior(repository.Object, UnitOfWork().Object, User().Object, Clock().Object);

        Func<Task> act = () => behavior
            .Handle(new IdempotentRequest("abc"), SuccessDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<IdempotencyConflictException>();
    }

    [Fact]
    public async Task Handle_ExpiredInProgressKey_MarksRejectedAndThrows() {
        DateTimeOffset old = FixedNow.AddMinutes(-11);
        var inProgress = new IdempotencyRecord(
            "test-key-abc",
            "actor-1",
            "IdempotentRequest",
            Fingerprint("abc"),
            old
        );

        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync("test-key-abc", "actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(inProgress);

        var behavior = Behavior(repository.Object, UnitOfWork().Object, User().Object, Clock().Object);

        Func<Task> act = () => behavior
            .Handle(new IdempotentRequest("abc"), SuccessDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<IdempotencyConflictException>();
        repository.Verify(r => r.Update(It.Is<IdempotencyRecord>(rec => rec.Status == IdempotencyStatus.Rejected)), Times.Once);
    }

    [Fact]
    public async Task Handle_BusinessFailure_MarksRejectedAndReturnsFailure() {
        var repository = EmptyRepository();

        var behavior = Behavior(repository.Object, UnitOfWork().Object, User().Object, Clock().Object);

        Result<Unit> result = await behavior.Handle(
            new IdempotentRequest("abc"),
            FailureDelegate(),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        repository.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.Update(It.Is<IdempotencyRecord>(rec => rec.Status == IdempotencyStatus.Rejected)), Times.Once);
        repository.Verify(r => r.Delete(It.IsAny<IdempotencyRecord>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HandlerThrows_LeavesOutcomeUnknownAndRethrows() {
        var repository = EmptyRepository();

        var behavior = Behavior(repository.Object, UnitOfWork().Object, User().Object, Clock().Object);

        Func<Task> act = () => behavior
            .Handle(new IdempotentRequest("abc"), ThrowingDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        repository.Verify(r => r.Update(It.IsAny<IdempotencyRecord>()), Times.Never);
        repository.Verify(r => r.Delete(It.IsAny<IdempotencyRecord>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HandlerCancellation_LeavesOutcomeUnknownAndDoesNotReject() {
        var repository = EmptyRepository();
        using var cancellation = new CancellationTokenSource();

        var behavior = Behavior(repository.Object, UnitOfWork().Object, User().Object, Clock().Object);

        Func<Task> act = () => behavior
            .Handle(
                new IdempotentRequest("abc"),
                CancellingDelegate(),
                cancellation.Token
            )
            .AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
        repository.Verify(
            r => r.Update(It.IsAny<IdempotencyRecord>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_WithoutAuthenticatedActor_ThrowsUnauthenticated() {
        var repository = EmptyRepository();

        var behavior = Behavior(repository.Object, UnitOfWork().Object, User(userId: null!).Object, Clock().Object);

        Func<Task> act = () => behavior
            .Handle(new IdempotentRequest("abc"), SuccessDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<UnauthenticatedException>();
        repository.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithoutCallerSuppliedKey_ThrowsMissingKeyError() {
        var repository = EmptyRepository();
        var behavior = new IdempotencyBehavior<MissingKeyRequest, Result<Unit>>(
            repository.Object,
            UnitOfWork().Object,
            User().Object,
            Clock().Object,
            NullLogger<IdempotencyBehavior<MissingKeyRequest, Result<Unit>>>.Instance
        );

        Func<Task> act = () => behavior
            .Handle(
                new MissingKeyRequest("abc"),
                (_, _) => ValueTask.FromResult(Result.Success(Unit.Value)),
                CancellationToken.None
            )
            .AsTask();

        await act.Should().ThrowAsync<MissingIdempotencyKeyException>();
        repository.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithOversizedCallerKey_ThrowsValidationError() {
        var repository = EmptyRepository();
        var behavior = Behavior(repository.Object, UnitOfWork().Object, User().Object, Clock().Object);

        Func<Task> act = () => behavior
            .Handle(
                new IdempotentRequest(new string('x', 200)),
                SuccessDelegate(),
                CancellationToken.None
            )
            .AsTask();

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        repository.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SystemActor_DoesNotRequireAuthenticatedUser() {
        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync("system-key-abc", "system:test", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        var behavior = new IdempotencyBehavior<SystemIdempotentRequest, Result<Unit>>(
            repository.Object,
            UnitOfWork().Object,
            User(userId: null!).Object,
            Clock().Object,
            NullLogger<IdempotencyBehavior<SystemIdempotentRequest, Result<Unit>>>.Instance
        );

        Result<Unit> result = await behavior.Handle(
            new SystemIdempotentRequest("abc"),
            (_, _) => ValueTask.FromResult(Result.Success(Unit.Value)),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        repository.Verify(
            r => r.GetAsync("system-key-abc", "system:test", It.IsAny<CancellationToken>()),
            Times.Once
        );
    }
}
