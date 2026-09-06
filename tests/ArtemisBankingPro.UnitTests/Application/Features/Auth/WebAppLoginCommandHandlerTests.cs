using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Enums;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Auth;

/// <summary>
/// Verifica el login de la aplicación web (spec §72-§126): valida credenciales
/// contra los roles MVC (Administrador, Cajero, Cliente), nunca genera JWT y
/// devuelve los mensajes exactos del contrato funcional, diferenciados del
/// login de la API ("aplicación web" vs "API").
/// </summary>
public sealed class WebAppLoginCommandHandlerTests {
    private static (
        WebAppLoginCommandHandler Handler,
        Mock<IUserAccountService> UserAccountService
    ) CreateHandler(LoginResult loginResult) {
        var userAccountService = new Mock<IUserAccountService>();
        userAccountService
            .Setup(service => service.ValidateCredentialsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginResult);

        return (
            new WebAppLoginCommandHandler(userAccountService.Object),
            userAccountService
        );
    }

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsUserIdentity() {
        var (handler, _) = CreateHandler(
            new LoginResult(
                LoginStatus.Success,
                UserId: "user-1",
                UserName: "cliente01",
                Role: nameof(Roles.Cliente)
            )
        );

        var result = await handler.Handle(
            new WebAppLoginCommand("cliente01", "P@ssw0rd123!"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be("user-1");
        result.Value.UserName.Should().Be("cliente01");
        result.Value.Role.Should().Be(nameof(Roles.Cliente));
    }

    [Fact]
    public async Task Handle_InvalidCredentials_ReturnsSpecMessage() {
        var (handler, _) = CreateHandler(
            new LoginResult(LoginStatus.InvalidCredentials, null, null, null)
        );

        var result = await handler.Handle(
            new WebAppLoginCommand("cliente01", "incorrecta"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be("Los datos de acceso son inválidos.");
    }

    [Fact]
    public async Task Handle_InactiveUser_ReturnsSpecActivationMessage() {
        var (handler, _) = CreateHandler(
            new LoginResult(LoginStatus.Inactive, "user-1", "cliente01", nameof(Roles.Cliente))
        );

        var result = await handler.Handle(
            new WebAppLoginCommand("cliente01", "P@ssw0rd123!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be(
            "Su cuenta se encuentra inactiva. Debe activar su cuenta mediante el enlace "
                + "enviado a su correo electrónico registrado para poder acceder al sistema."
        );
    }

    [Fact]
    public async Task Handle_RoleNotAllowedForWebApp_ReturnsWebAppMessage() {
        var (handler, _) = CreateHandler(
            new LoginResult(
                LoginStatus.RoleNotAllowed,
                "user-1",
                "commerce01",
                nameof(Roles.Comercio)
            )
        );

        var result = await handler.Handle(
            new WebAppLoginCommand("commerce01", "P@ssw0rd123!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        // El rol Comercio no accede a la aplicación web; el mensaje es el de la
        // spec §118-120 ("aplicación web"), distinto del login de la API.
        result.Error!.Message.Should().Be(
            "Este usuario no tiene permisos para acceder a la aplicación web."
        );
    }

    [Fact]
    public async Task Handle_UsesMvcRoleSet() {
        var (handler, userAccountService) = CreateHandler(
            new LoginResult(
                LoginStatus.Success,
                UserId: "user-1",
                UserName: "cajero01",
                Role: nameof(Roles.Cajero)
            )
        );

        await handler.Handle(
            new WebAppLoginCommand("cajero01", "P@ssw0rd123!"),
            CancellationToken.None
        );

        // Solo los roles de la aplicación web pueden iniciar sesión por MVC
        // (spec §109-110); Comercio queda fuera de este conjunto.
        userAccountService.Verify(
            service => service.ValidateCredentialsAsync(
                "cajero01",
                "P@ssw0rd123!",
                RoleSets.Mvc,
                It.IsAny<CancellationToken>()),
            Times.Once
        );
    }
}
