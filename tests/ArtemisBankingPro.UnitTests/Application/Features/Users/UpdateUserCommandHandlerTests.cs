using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Features.Users.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Operations.Entities;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Users;

/// <summary>
/// Verifica la edición de usuarios (spec §823-§922 y API PUT /api/users/{id}):
/// unicidad excluyendo al propio usuario, contraseña opcional, y monto
/// adicional solo para Cliente o Comercio acreditado a la cuenta principal.
/// </summary>
public sealed class UpdateUserCommandHandlerTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(-4));

    private sealed class Fixture {
        public Mock<IUserAccountService> UserAccountService { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<ISavingsAccountRepository> Accounts { get; } = new();
        public Mock<IFinancialOperationRepository> Operations { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IBusinessClock> Clock { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();

        public Fixture() {
            Clock.SetupGet(clock => clock.Now).Returns(Now);
            CurrentUser.SetupGet(user => user.UserId).Returns("admin-1");
            UnitOfWork
                .Setup(unit => unit.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result>>>(),
                    It.IsAny<System.Data.IsolationLevel>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (Func<CancellationToken, Task<Result>> operation,
                        System.Data.IsolationLevel _,
                        CancellationToken ct) => operation(ct)
                );
            UserAccountService
                .Setup(service => service.UpdateUserProfileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success());
        }

        public UpdateUserCommandHandler Handler =>
            new(
                UserAccountService.Object,
                Users.Object,
                Accounts.Object,
                Operations.Object,
                UnitOfWork.Object,
                Clock.Object,
                CurrentUser.Object
            );

        public void SeedUser(string role = nameof(Roles.Cliente)) {
            Users
                .Setup(repository => repository.GetByIdAsync("user-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new UserListDto(
                        "user-1",
                        "cliente01",
                        "00187654321",
                        "María",
                        "Gómez",
                        "cliente01@example.com",
                        role,
                        IsActive: true,
                        CreatedAt: Now)
                );
        }

        public void SeedPrincipalAccount(decimal balance = 5_000m) {
            Accounts
                .Setup(repository => repository.GetPrincipalByOwnerAsync(
                    "user-1",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    SavingsAccount
                        .OpenPrimary(
                            "user-1",
                            AccountNumber.Create("100000001").Value,
                            Money.Create(balance).Value,
                            "admin",
                            Now)
                        .Value);
        }
    }

    private static UpdateUserCommand ValidCommand(decimal? additionalAmount = null) =>
        new(
            UserId: "user-1",
            FirstName: "María",
            LastName: "Gómez",
            Identification: "00187654321",
            Email: "cliente01@example.com",
            UserName: "cliente01",
            AdditionalAmount: additionalAmount
        );

    [Fact]
    public async Task Handle_UnknownUser_ReturnsNotFound() {
        var fixture = new Fixture();
        fixture.Users
            .Setup(repository => repository.GetByIdAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserListDto?)null);

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be("El usuario seleccionado no existe.");
    }

    [Fact]
    public async Task Handle_DuplicateUserName_ReturnsConflict() {
        var fixture = new Fixture();
        fixture.SeedUser();
        fixture.Users
            .Setup(repository => repository.ExistsByUserNameAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.Handler.Handle(
            ValidCommand() with { UserName = "otrousuario" },
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be(
            "Ya existe otro usuario registrado con este nombre de usuario."
        );
    }

    [Fact]
    public async Task Handle_SameUserNameAsOwn_IsAllowed() {
        var fixture = new Fixture();
        fixture.SeedUser();
        fixture.Users
            .Setup(repository => repository.ExistsByUserNameAsync(
                "cliente01",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithNewPassword_ChangesPassword() {
        var fixture = new Fixture();
        fixture.SeedUser();
        fixture.UserAccountService
            .Setup(service => service.ChangePasswordAsync(
                "user-1",
                "NuevaClave123!",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await fixture.Handler.Handle(
            ValidCommand() with { Password = "NuevaClave123!", ConfirmPassword = "NuevaClave123!" },
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        fixture.UserAccountService.Verify(
            service => service.ChangePasswordAsync(
                "user-1",
                "NuevaClave123!",
                It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_WithoutPassword_DoesNotChangePassword() {
        var fixture = new Fixture();
        fixture.SeedUser();

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.UserAccountService.Verify(
            service => service.ChangePasswordAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_AdditionalAmountForClient_CreditsPrincipalAccount() {
        var fixture = new Fixture();
        fixture.SeedUser(nameof(Roles.Cliente));
        fixture.SeedPrincipalAccount(balance: 5_000m);

        var result = await fixture.Handler.Handle(
            ValidCommand(additionalAmount: 12_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        fixture.Operations.Verify(
            repository => repository.AddAsync(
                It.IsAny<FinancialOperation>(),
                It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_AdditionalAmountWithoutPrincipalAccount_Fails() {
        var fixture = new Fixture();
        fixture.SeedUser(nameof(Roles.Cliente));
        fixture.Accounts
            .Setup(repository => repository.GetPrincipalByOwnerAsync(
                "user-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavingsAccount?)null);

        var result = await fixture.Handler.Handle(
            ValidCommand(additionalAmount: 12_000m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be(
            "El usuario no tiene una cuenta de ahorro principal activa."
        );
    }

    [Fact]
    public async Task Handle_AdditionalAmountForNonClient_IsIgnored() {
        var fixture = new Fixture();
        fixture.SeedUser(nameof(Roles.Administrador));

        var result = await fixture.Handler.Handle(
            ValidCommand(additionalAmount: 12_000m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        fixture.Operations.Verify(
            repository => repository.AddAsync(
                It.IsAny<FinancialOperation>(),
                It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}
