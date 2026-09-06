using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;
using ArtemisBankingPro.Application.Common.Behaviors;
using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Interfaces.Identity;
using Mediator;

namespace ArtemisBankingPro.UnitTests.Application.Common.Behaviors;

public sealed record AuthorizedRequest : IRequest<Result<Unit>>, IAuthorize {
    public string[] RequiredRoles => ["Administrador"];
}

public sealed record OwnedRequest : IRequest<Result<Unit>>, IAuthorize, IOwnershipCheck {
    public string[] RequiredRoles => ["Cliente"];

    public Task VerifyOwnershipAsync(ICurrentUserService currentUser, CancellationToken ct) =>
        currentUser.UserId == "owner-1"
            ? Task.CompletedTask
            : throw new ForbiddenAccessException();
}

public sealed class AuthorizationBehaviorTests {
    private static MessageHandlerDelegate<AuthorizedRequest, Result<Unit>> OkAuthorizedDelegate() =>
        (_, _) => ValueTask.FromResult(Result.Success(Unit.Value));

    private static MessageHandlerDelegate<OwnedRequest, Result<Unit>> OkOwnedDelegate() =>
        (_, _) => ValueTask.FromResult(Result.Success(Unit.Value));

    private static Mock<ICurrentUserService> AuthenticatedUser(string role, string userId = "user-1") {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        user.SetupGet(u => u.UserId).Returns(userId);
        user.SetupGet(u => u.Role).Returns(role);
        return user;
    }

    [Fact]
    public async Task Handle_WhenNotAuthenticated_ThrowsUnauthenticated() {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.IsAuthenticated).Returns(false);
        user.SetupGet(u => u.UserId).Returns((string?)null);

        var behavior = new AuthorizationBehavior<AuthorizedRequest, Result<Unit>>(user.Object);

        Func<Task> act = () => behavior
            .Handle(new AuthorizedRequest(), OkAuthorizedDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<UnauthenticatedException>();
    }

    [Fact]
    public async Task Handle_WhenUserIdNull_ThrowsUnauthenticated() {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        user.SetupGet(u => u.UserId).Returns((string?)null);
        user.SetupGet(u => u.Role).Returns("Administrador");

        var behavior = new AuthorizationBehavior<AuthorizedRequest, Result<Unit>>(user.Object);

        Func<Task> act = () => behavior
            .Handle(new AuthorizedRequest(), OkAuthorizedDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<UnauthenticatedException>();
    }

    [Fact]
    public async Task Handle_WhenRoleNotAllowed_ThrowsForbidden() {
        var behavior = new AuthorizationBehavior<AuthorizedRequest, Result<Unit>>(
            AuthenticatedUser("Cliente").Object
        );

        Func<Task> act = () => behavior
            .Handle(new AuthorizedRequest(), OkAuthorizedDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task Handle_WhenRoleAllowed_InvokesNext() {
        var behavior = new AuthorizationBehavior<AuthorizedRequest, Result<Unit>>(
            AuthenticatedUser("Administrador").Object
        );
        bool invoked = false;

        MessageHandlerDelegate<AuthorizedRequest, Result<Unit>> next = (_, _) => {
            invoked = true;
            return ValueTask.FromResult(Result.Success(Unit.Value));
        };

        var result = await behavior.Handle(new AuthorizedRequest(), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenOwnershipFails_ThrowsForbidden() {
        var behavior = new AuthorizationBehavior<OwnedRequest, Result<Unit>>(
            AuthenticatedUser("Cliente", userId: "other-1").Object
        );

        Func<Task> act = () => behavior
            .Handle(new OwnedRequest(), OkOwnedDelegate(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task Handle_WhenOwnershipPasses_InvokesNext() {
        var behavior = new AuthorizationBehavior<OwnedRequest, Result<Unit>>(
            AuthenticatedUser("Cliente", userId: "owner-1").Object
        );
        bool invoked = false;

        MessageHandlerDelegate<OwnedRequest, Result<Unit>> next = (_, _) => {
            invoked = true;
            return ValueTask.FromResult(Result.Success(Unit.Value));
        };

        var result = await behavior.Handle(new OwnedRequest(), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invoked.Should().BeTrue();
    }
}
