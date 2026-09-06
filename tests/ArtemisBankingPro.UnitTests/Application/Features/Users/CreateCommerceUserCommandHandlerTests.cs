using ArtemisBankingPro.Application.Features.Users.Commands;
using ArtemisBankingPro.Application.Features.Users.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Merchants.Entities;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArtemisBankingPro.UnitTests.Application.Features.Users;

/// <summary>
/// Verifica la creación del usuario de comercio (spec §4737-§4844 y API
/// POST /api/users/commerce/{commerceId}): el comercio debe existir y no
/// tener usuario asociado, la identidad debe ser única, y el usuario nace
/// inactivo con su cuenta de ahorro principal.
/// </summary>
public sealed class CreateCommerceUserCommandHandlerTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(-4));

    private sealed class Fixture {
        public Mock<IUserAccountService> UserAccountService { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IMerchantRepository> Merchants { get; } = new();
        public Mock<ISavingsAccountRepository> Accounts { get; } = new();
        public Mock<IFinancialOperationRepository> Operations { get; } = new();
        public Mock<IAccountTokenService> Tokens { get; } = new();
        public Mock<IEmailService> Emails { get; } = new();
        public Mock<INumberGenerator> NumberGenerator { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IBusinessClock> Clock { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();

        public Fixture() {
            Clock.SetupGet(clock => clock.Now).Returns(Now);
            CurrentUser.SetupGet(user => user.UserId).Returns("admin-1");
            NumberGenerator
                .Setup(generator => generator.NextAccountNumberAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync("100000001");
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
            UnitOfWork
                .Setup(unit => unit.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result<string>>>>(),
                    It.IsAny<System.Data.IsolationLevel>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (Func<CancellationToken, Task<Result<string>>> operation,
                        System.Data.IsolationLevel _,
                        CancellationToken ct) => operation(ct)
                );
            Emails
                .Setup(service => service.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<IEmailModel>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Tokens
                .Setup(service => service.GenerateAsync(
                    It.IsAny<string>(),
                    AccountTokenType.Activation,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("token-de-activacion");
        }

        public CreateCommerceUserCommandHandler Handler =>
            new(
                UserAccountService.Object,
                Users.Object,
                Merchants.Object,
                Accounts.Object,
                Operations.Object,
                Tokens.Object,
                Emails.Object,
                NumberGenerator.Object,
                UnitOfWork.Object,
                Clock.Object,
                CurrentUser.Object,
                NullLogger<CreateCommerceUserCommandHandler>.Instance
            );

        private static Merchant CreateMerchant(string? associatedUserId = null) {
            Merchant merchant = Merchant
                .Create(
                    "Tienda Demo",
                    "Comercio de prueba",
                    "contacto@tiendademo.com",
                    "8095551234",
                    "101999999",
                    "admin-1",
                    Now)
                .Value;
            if (associatedUserId is not null) {
                merchant.AssociateUser(associatedUserId, Now);
            }

            return merchant;
        }

        public void SeedMerchant(string? associatedUserId = null) {
            Merchants
                .Setup(repository => repository.GetByIdAsync(5, It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    CreateMerchant(associatedUserId));
        }
    }

    private static CreateCommerceUserCommand ValidCommand() =>
        new(
            CommerceId: 5,
            FirstName: "Usuario",
            LastName: "Comercio",
            Identification: "10199999999",
            Email: "commerce01@example.com",
            UserName: "commerce01",
            Password: "P@ssw0rd123!",
            ConfirmPassword: "P@ssw0rd123!",
            InitialAmount: 500m
        );

    [Fact]
    public async Task Handle_ValidCommerce_CreatesInactiveUserWithPrincipalAccount() {
        var fixture = new Fixture();
        fixture.SeedMerchant();
        fixture.UserAccountService
            .Setup(service => service.CreateUserAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                nameof(Roles.Comercio),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Result.Success(
                    new CreatedUserInfo(
                        "user-10",
                        "commerce01",
                        "commerce01@example.com",
                        nameof(Roles.Comercio),
                        IsActive: false,
                        FullName: "Usuario Comercio")
                )
            );

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();
        result.Value.Role.Should().Be(nameof(Roles.Comercio));
        result.Value.CommerceId.Should().Be(5);
        // Cuenta principal creada para el usuario del comercio.
        fixture.Accounts.Verify(
            repository => repository.AddAsync(
                It.Is<SavingsAccount>(account =>
                    account.OwnerUserId == "user-10" && account.Type == AccountType.Primary),
                It.IsAny<CancellationToken>()),
            Times.Once
        );
        fixture.Emails.Verify(
            service => service.SendAsync(
                It.IsAny<string>(),
                It.IsAny<IEmailModel>(),
                It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_UnknownCommerce_ReturnsNotFound() {
        var fixture = new Fixture();
        fixture.Merchants
            .Setup(repository => repository.GetByIdAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Merchant?)null);

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be("El comercio indicado no existe.");
    }

    [Fact]
    public async Task Handle_CommerceWithUserAlreadyAssociated_ReturnsConflict() {
        var fixture = new Fixture();
        fixture.SeedMerchant(associatedUserId: "user-9");

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be("El comercio ya tiene un usuario asociado.");
        fixture.UserAccountService.Verify(
            service => service.CreateUserAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_DuplicateUserName_ReturnsConflict() {
        var fixture = new Fixture();
        fixture.SeedMerchant();
        fixture.Users
            .Setup(repository => repository.ExistsByUserNameAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be(
            "Ya existe un usuario registrado con este nombre de usuario."
        );
    }
}
