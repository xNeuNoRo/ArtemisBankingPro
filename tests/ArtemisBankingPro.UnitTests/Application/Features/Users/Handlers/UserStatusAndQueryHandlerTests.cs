using Moq;
using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Features.Users.Handlers;
using ArtemisBankingPro.Application.Features.Users.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;

namespace ArtemisBankingPro.UnitTests.Application.Features.Users.Handlers;

public sealed class ChangeUserStatusCommandHandlerTests
{
    [Fact]
    public async Task Handle_ActivatesUser_ReturnsSuccess()
    {
        var userService = new Mock<IUserAccountService>();
        userService
            .Setup(s => s.SetActiveAsync("user-1", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var handler = new ChangeUserStatusCommandHandler(userService.Object);

        var result = await handler.Handle(
            new ChangeUserStatusCommand("user-1", true),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        userService.Verify(
            s => s.SetActiveAsync("user-1", true, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_UnknownUser_ReturnsNotFound()
    {
        var userService = new Mock<IUserAccountService>();
        userService
            .Setup(s => s.SetActiveAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(DomainError.NotFound("User.NotFound", "El usuario no existe.")));

        var handler = new ChangeUserStatusCommandHandler(userService.Object);

        var result = await handler.Handle(
            new ChangeUserStatusCommand("ghost", true),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("User.NotFound");
    }
}

public sealed class GetUsersPagedQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsPagedUsers()
    {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(r => r.GetPagedAsync(
                It.IsAny<string?>(),
                It.IsAny<PageRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(
                new PageResult<UserListDto>(
                    [
                        new UserListDto(
                            "user-1",
                            "admin",
                            "00112345678",
                            "Juan",
                            "Pérez",
                            "admin@artemis.com",
                            "Administrador",
                            true,
                            DateTimeOffset.UtcNow
                        ),
                    ],
                    TotalCount: 1,
                    Page: 1,
                    PageSize: 20
                )
            );

        var handler = new GetUsersPagedQueryHandler(repository.Object);

        var result = await handler.Handle(
            new GetUsersPagedQuery(Role: "Administrador"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].UserName.Should().Be("admin");
        repository.Verify(
            r => r.GetPagedAsync("Administrador", It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }
}

public sealed class GetCommerceUsersPagedQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsCommerceUsers()
    {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(r => r.GetCommerceUsersPagedAsync(
                It.IsAny<PageRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(
                new PageResult<UserListDto>([], TotalCount: 0,
                    Page: 1, PageSize: 20)
            );

        var handler = new GetCommerceUsersPagedQueryHandler(repository.Object);

        var result = await handler.Handle(
            new GetCommerceUsersPagedQuery(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        repository.Verify(
            r => r.GetCommerceUsersPagedAsync(It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }
}

public sealed class GetUserByIdQueryHandlerTests
{
    private static readonly DateTimeOffset FixedCreatedAt =
        new(2026, 8, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_UnknownUser_ReturnsNotFound()
    {
        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserListDto?)null);
        var accountRepository = new Mock<ISavingsAccountRepository>();

        var handler = new GetUserByIdQueryHandler(userRepository.Object, accountRepository.Object);

        var result = await handler.Handle(
            new GetUserByIdQuery("ghost"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("User.NotFound");
    }

    [Fact]
    public async Task Handle_ExistingUserWithoutPrincipalAccount_ReturnsDetailWithNullMainAccount()
    {
        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new UserListDto(
                    "user-1",
                    "admin",
                    "00112345678",
                    "Juan",
                    "Pérez",
                    "admin@artemis.com",
                    "Administrador",
                    true,
                    FixedCreatedAt
                )
            );
        var accountRepository = new Mock<ISavingsAccountRepository>();
        accountRepository
            .Setup(r => r.GetPrincipalByOwnerAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArtemisBankingPro.Domain.Accounts.Entities.SavingsAccount?)null);

        var handler = new GetUserByIdQueryHandler(userRepository.Object, accountRepository.Object);

        var result = await handler.Handle(
            new GetUserByIdQuery("user-1"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.UserName.Should().Be("admin");
        result.Value.CreatedAt.Should().Be(FixedCreatedAt);
        Assert.Null(result.Value.MainAccount);
    }
}
