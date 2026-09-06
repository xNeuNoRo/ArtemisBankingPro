using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

/// <summary>
/// Verifica el login de la aplicación web (spec §72-§126) contra Identity
/// real: los roles Administrador, Cajero y Cliente acceden; el rol Comercio se
/// rechaza con el mensaje de la aplicación web (el login de la API sí lo
/// acepta); los usuarios inactivos y las credenciales inválidas devuelven los
/// mensajes exactos del contrato.
/// </summary>
[Collection("SqlServer")]
public sealed class WebAppLoginIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private async Task<AppUser> CreateUserAsync(string userName, string role, bool active = true) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Nombre",
            LastName = "Apellido",
            IdentityDocument = $"80000{userName.Length:00}1",
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();

        return user;
    }

    [Fact]
    public async Task Login_AdminUser_ReturnsMvcIdentity() {
        await CreateUserAsync("webadmin", nameof(Roles.Administrador));

        await using var scope = Fixture.Services.CreateAsyncScope();
        var handler = new WebAppLoginCommandHandler(
            scope.ServiceProvider.GetRequiredService<IUserAccountService>()
        );
        var result = await handler.Handle(
            new WebAppLoginCommand("webadmin", "P@ssw0rd123!"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be(nameof(Roles.Administrador));
        result.Value.UserName.Should().Be("webadmin");
        result.Value.UserId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_CashierAndClientUsers_ReturnMvcIdentity() {
        await CreateUserAsync("webcajero", nameof(Roles.Cajero));
        await CreateUserAsync("webcliente", nameof(Roles.Cliente));

        await using var scope = Fixture.Services.CreateAsyncScope();
        var handler = new WebAppLoginCommandHandler(
            scope.ServiceProvider.GetRequiredService<IUserAccountService>()
        );
        var cashier = await handler.Handle(
            new WebAppLoginCommand("webcajero", "P@ssw0rd123!"),
            CancellationToken.None
        );
        var client = await handler.Handle(
            new WebAppLoginCommand("webcliente", "P@ssw0rd123!"),
            CancellationToken.None
        );

        cashier.IsSuccess.Should().BeTrue();
        cashier.Value.Role.Should().Be(nameof(Roles.Cajero));
        client.IsSuccess.Should().BeTrue();
        client.Value.Role.Should().Be(nameof(Roles.Cliente));
    }

    [Fact]
    public async Task Login_CommerceUser_RejectedWithWebAppMessage() {
        // El rol Comercio está activo y podría autenticarse en la API, pero la
        // aplicación web lo rechaza con su mensaje propio (spec §86-88, §118-120).
        await CreateUserAsync("webcomercio", nameof(Roles.Comercio));

        await using var scope = Fixture.Services.CreateAsyncScope();
        var handler = new WebAppLoginCommandHandler(
            scope.ServiceProvider.GetRequiredService<IUserAccountService>()
        );
        var result = await handler.Handle(
            new WebAppLoginCommand("webcomercio", "P@ssw0rd123!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be(
            "Este usuario no tiene permisos para acceder a la aplicación web."
        );
    }

    [Fact]
    public async Task Login_InactiveUser_ReturnsActivationMessage() {
        await CreateUserAsync("webinactivo", nameof(Roles.Cliente), active: false);

        await using var scope = Fixture.Services.CreateAsyncScope();
        var handler = new WebAppLoginCommandHandler(
            scope.ServiceProvider.GetRequiredService<IUserAccountService>()
        );
        var result = await handler.Handle(
            new WebAppLoginCommand("webinactivo", "P@ssw0rd123!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be(
            "Su cuenta se encuentra inactiva. Debe activar su cuenta mediante el enlace "
                + "enviado a su correo electrónico registrado para poder acceder al sistema."
        );
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsInvalidCredentialsMessage() {
        await CreateUserAsync("webcredenciales", nameof(Roles.Cliente));

        await using var scope = Fixture.Services.CreateAsyncScope();
        var handler = new WebAppLoginCommandHandler(
            scope.ServiceProvider.GetRequiredService<IUserAccountService>()
        );
        var result = await handler.Handle(
            new WebAppLoginCommand("webcredenciales", "contraseña-incorrecta"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be("Los datos de acceso son inválidos.");
    }
}
