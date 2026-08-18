using Moq;
using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.DTOs;
using ArtemisBankingPro.Application.Features.Auth.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Merchants.Entities;

namespace ArtemisBankingPro.UnitTests.Application.Features.Auth.Handlers;

public sealed class LoginCommandHandlerTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static Mock<IBusinessClock> Clock() {
        var clock = new Mock<IBusinessClock>();
        clock.SetupGet(c => c.NowUtc).Returns(FixedNow);
        return clock;
    }

    private static Mock<IJwtTokenService> JwtService(string token = "jwt-token") {
        var jwt = new Mock<IJwtTokenService>();
        jwt
            .Setup(j => j.GenerateToken(It.IsAny<JwtTokenRequest>()))
            .Returns(new JwtTokenResult(token, FixedNow.AddMinutes(15)));
        return jwt;
    }

    private static Mock<IUserAccountService> UserService(LoginResult result) {
        var service = new Mock<IUserAccountService>();
        service
            .Setup(s => s.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return service;
    }

    [Fact]
    public async Task Handle_SuccessfulAdminLogin_ReturnsJwtAndUserInfo() {
        var userService = UserService(
            new LoginResult(LoginStatus.Success, "user-1", "admin", "Administrador")
        );
        var jwtService = JwtService();
        var merchantRepo = new Mock<IMerchantRepository>();
        var handler = new LoginCommandHandler(
            userService.Object,
            jwtService.Object,
            merchantRepo.Object,
            Clock().Object
        );

        var result = await handler.Handle(
            new LoginCommand("admin", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        Assert.Equal(
            new LoginResponse("jwt-token", FixedNow.AddMinutes(15), "user-1", "admin", "Administrador"),
            result.Value
        );
        jwtService.Verify(
            j => j.GenerateToken(
                It.Is<JwtTokenRequest>(r =>
                    r.UserId == "user-1"
                    && r.UserName == "admin"
                    && r.Role == "Administrador"
                    && r.CommerceId == null
                    && r.IssuedAtUtc == FixedNow
                )
            ),
            Times.Once
        );
        merchantRepo.Verify(
            m => m.GetByAssociatedUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_SuccessfulCommerceLogin_ResolvesCommerceId() {
        var userService = UserService(
            new LoginResult(LoginStatus.Success, "user-2", "comercio01", "Comercio")
        );
        var jwtService = JwtService();
        var merchantRepo = new Mock<IMerchantRepository>();
        merchantRepo
            .Setup(m => m.GetByAssociatedUserIdAsync("user-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Merchant.Create(
                    "Tienda Demo",
                    null,
                    "comercio@demo.com",
                    "809-555-1234",
                    "131234567",
                    "user-0",
                    FixedNow
                ).Value
            );

        var handler = new LoginCommandHandler(
            userService.Object,
            jwtService.Object,
            merchantRepo.Object,
            Clock().Object
        );

        var result = await handler.Handle(
            new LoginCommand("comercio01", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        jwtService.Verify(
            j => j.GenerateToken(It.Is<JwtTokenRequest>(r => r.CommerceId != null)),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_InvalidCredentials_ReturnsUnauthorized() {
        var handler = new LoginCommandHandler(
            UserService(new LoginResult(LoginStatus.InvalidCredentials, null, null, null)).Object,
            JwtService().Object,
            new Mock<IMerchantRepository>().Object,
            Clock().Object
        );

        var result = await handler.Handle(
            new LoginCommand("admin", "wrong"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Category.Should().Be(ErrorCategory.Unauthorized);
        result.Error.Code.Should().Be("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task Handle_InactiveUser_ReturnsUnauthorizedWithActivationMessage() {
        var handler = new LoginCommandHandler(
            UserService(new LoginResult(LoginStatus.Inactive, "user-1", "admin", null)).Object,
            JwtService().Object,
            new Mock<IMerchantRepository>().Object,
            Clock().Object
        );

        var result = await handler.Handle(
            new LoginCommand("admin", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Category.Should().Be(ErrorCategory.Unauthorized);
        result.Error.Code.Should().Be("Auth.Inactive");
        result.Error.Message.Should().Be(
            "Su cuenta se encuentra inactiva. Debe activar su cuenta mediante el enlace "
                + "enviado a su correo electrónico registrado para poder acceder al sistema."
        );
    }

    [Fact]
    public async Task Handle_RoleNotAllowed_ReturnsForbidden() {
        var handler = new LoginCommandHandler(
            UserService(new LoginResult(LoginStatus.RoleNotAllowed, "user-3", "cliente01", null)).Object,
            JwtService().Object,
            new Mock<IMerchantRepository>().Object,
            Clock().Object
        );

        var result = await handler.Handle(
            new LoginCommand("cliente01", "123P@$$word!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Category.Should().Be(ErrorCategory.Forbidden);
        result.Error.Code.Should().Be("Auth.RoleNotAllowed");
        result.Error.Message.Should().Be("Este usuario no tiene permisos para acceder a la API.");
    }
}
