using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;
using ArtemisBankingPro.Application.Common.Behaviors;
using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using Mediator;

namespace ArtemisBankingPro.UnitTests.Application.Common.Behaviors;

public sealed record IdempotentRequest(string Payload) : IRequest<Result<Unit>>, IIdempotentCommand {
    public string IdempotencyKey => $"test-key-{Payload}";
    public string RequestFingerprint => Payload;
}

public sealed class IdempotencyBehaviorTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

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

    private static MessageHandlerDelegate<IdempotentRequest, Result<Unit>> SuccessDelegate() =>
        (_, _) => ValueTask.FromResult(Result.Success(Unit.Value));

    private static MessageHandlerDelegate<IdempotentRequest, Result<Unit>> FailureDelegate() =>
        (_, _) => ValueTask.FromResult(
            Result.Failure<Unit>(DomainError.Declined("Test.Declined", "Rechazado."))
        );

    private static MessageHandlerDelegate<IdempotentRequest, Result<Unit>> ThrowingDelegate() =>
        (_, _) => throw new InvalidOperationException("Handler failed.");

    [Fact]
    public async Task Handle_FirstExecution_CreatesRecordAndCompletesIt() {
        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        var behavior = new IdempotencyBehavior<IdempotentRequest, Result<Unit>>(
            repository.Object,
            UnitOfWork().Object,
            User().Object,
            Clock().Object
        );

        var result = await behavior.Handle(
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
        var completed = new IdempotencyRecord("test-key-abc", "actor-1", "IdempotentRequest", "abc", FixedNow);
        completed.Complete("test-key-abc", FixedNow);

        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync("test-key-abc", "actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(completed);

        var behavior = new IdempotencyBehavior<IdempotentRequest, Result<Unit>>(
            repository.Object,
            UnitOfWork().Object,
            User().Object,
            Clock().Object
        );

        Func<Task> act = () => behavior
            .Handle(new IdempotentRequest("abc"), SuccessDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<IdempotencyConflictException>();
        repository.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SameKeyDifferentFingerprint_ThrowsIdempotencyConflict() {
        var existing = new IdempotencyRecord("test-key-abc", "actor-1", "IdempotentRequest", "original", FixedNow);

        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync("test-key-abc", "actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var behavior = new IdempotencyBehavior<IdempotentRequest, Result<Unit>>(
            repository.Object,
            UnitOfWork().Object,
            User().Object,
            Clock().Object
        );

        Func<Task> act = () => behavior
            .Handle(new IdempotentRequest("abc"), SuccessDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<IdempotencyConflictException>();
    }

    [Fact]
    public async Task Handle_InProgressKey_ThrowsIdempotencyConflict() {
        var inProgress = new IdempotencyRecord("test-key-abc", "actor-1", "IdempotentRequest", "abc", FixedNow);

        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync("test-key-abc", "actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(inProgress);

        var behavior = new IdempotencyBehavior<IdempotentRequest, Result<Unit>>(
            repository.Object,
            UnitOfWork().Object,
            User().Object,
            Clock().Object
        );

        Func<Task> act = () => behavior
            .Handle(new IdempotentRequest("abc"), SuccessDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<IdempotencyConflictException>();
    }

    [Fact]
    public async Task Handle_BusinessFailure_DeletesRecordAndReturnsFailure() {
        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        var behavior = new IdempotencyBehavior<IdempotentRequest, Result<Unit>>(
            repository.Object,
            UnitOfWork().Object,
            User().Object,
            Clock().Object
        );

        var result = await behavior.Handle(
            new IdempotentRequest("abc"),
            FailureDelegate(),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        repository.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.Delete(It.IsAny<IdempotencyRecord>()), Times.Once);
        repository.Verify(r => r.Update(It.IsAny<IdempotencyRecord>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HandlerThrows_DeletesRecordAndRethrows() {
        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        var behavior = new IdempotencyBehavior<IdempotentRequest, Result<Unit>>(
            repository.Object,
            UnitOfWork().Object,
            User().Object,
            Clock().Object
        );

        Func<Task> act = () => behavior
            .Handle(new IdempotentRequest("abc"), ThrowingDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        repository.Verify(r => r.Delete(It.IsAny<IdempotencyRecord>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithoutAuthenticatedActor_ThrowsUnauthenticated() {
        var repository = new Mock<IIdempotencyRecordRepository>();
        repository
            .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        var behavior = new IdempotencyBehavior<IdempotentRequest, Result<Unit>>(
            repository.Object,
            UnitOfWork().Object,
            User(userId: null!).Object,
            Clock().Object
        );

        Func<Task> act = () => behavior
            .Handle(new IdempotentRequest("abc"), SuccessDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<UnauthenticatedException>();
        repository.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
