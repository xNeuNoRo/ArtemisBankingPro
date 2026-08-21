using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

/// <summary>Stub de email para flujos de auth en tests: registra el último modelo enviado.</summary>
public sealed class EmailCaptureService : IEmailService {
    public IEmailModel? LastModel { get; private set; }

    public Task SendAsync<T>(string recipient, T model, CancellationToken ct = default)
        where T : IEmailModel {
        LastModel = model;
        return Task.CompletedTask;
    }
}

[Collection("SqlServer")]
public sealed class AuthFlowIntegrationTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private async Task<AppUser> CreateUserAsync(string userName, string role, bool active = true) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync(role)) {
            await roleManager.CreateAsync(new IdentityRole(role));
        }

        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Nombre",
            LastName = "Apellido",
            IdentityDocument = $"9000{userName.Length}0001",
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();

        return user;
    }

    [Fact]
    public async Task Login_AdminUser_ReturnsValidJwt() {
        await CreateUserAsync("authadmin", nameof(Roles.Administrador));

        await using var scope = Fixture.Services.CreateAsyncScope();
        var handler = new LoginCommandHandler(
            scope.ServiceProvider.GetRequiredService<IUserAccountService>(),
            scope.ServiceProvider.GetRequiredService<IJwtTokenService>(),
            scope.ServiceProvider.GetRequiredService<IMerchantRepository>(),
            scope.ServiceProvider.GetRequiredService<IBusinessClock>()
        );

        var result = await handler.Handle(
            new LoginCommand("authadmin", "P@ssw0rd123!"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Jwt.Should().NotBeNullOrWhiteSpace();
        result.Value.Role.Should().Be(nameof(Roles.Administrador));
        result.Value.UserId.Should().NotBeNullOrWhiteSpace();
        result.Value.ExpiresAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_InactiveUser_ReturnsUnauthorized() {
        await CreateUserAsync("authinactive", nameof(Roles.Administrador), active: false);

        await using var scope = Fixture.Services.CreateAsyncScope();
        var handler = new LoginCommandHandler(
            scope.ServiceProvider.GetRequiredService<IUserAccountService>(),
            scope.ServiceProvider.GetRequiredService<IJwtTokenService>(),
            scope.ServiceProvider.GetRequiredService<IMerchantRepository>(),
            scope.ServiceProvider.GetRequiredService<IBusinessClock>()
        );

        var result = await handler.Handle(
            new LoginCommand("authinactive", "P@ssw0rd123!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Auth.Inactive");
    }

    [Fact]
    public async Task Login_ClientRole_ReturnsForbidden() {
        await CreateUserAsync("authclient", nameof(Roles.Cliente));

        await using var scope = Fixture.Services.CreateAsyncScope();
        var handler = new LoginCommandHandler(
            scope.ServiceProvider.GetRequiredService<IUserAccountService>(),
            scope.ServiceProvider.GetRequiredService<IJwtTokenService>(),
            scope.ServiceProvider.GetRequiredService<IMerchantRepository>(),
            scope.ServiceProvider.GetRequiredService<IBusinessClock>()
        );

        var result = await handler.Handle(
            new LoginCommand("authclient", "P@ssw0rd123!"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Auth.RoleNotAllowed");
    }

    [Fact]
    public async Task Login_repeated_wrong_password_locks_out_the_identity_user() {
        AppUser user = await CreateUserAsync("authlockout", nameof(Roles.Administrador));

        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserAccountService>();

        for (var attempt = 0; attempt < 5; attempt++) {
            LoginResult result = await service.ValidateCredentialsAsync(
                "authlockout",
                "WrongP@ssword123!",
                RoleSets.Api
            );

            result.Status.Should().Be(LoginStatus.InvalidCredentials);
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        AppUser locked = (await userManager.FindByIdAsync(user.Id))!;
        locked.LockoutEnd.Should().NotBeNull();

        LoginResult validPassword = await service.ValidateCredentialsAsync(
            "authlockout",
            "P@ssw0rd123!",
            RoleSets.Api
        );
        validPassword.Status.Should().Be(LoginStatus.InvalidCredentials);
    }

    [Fact]
    public async Task ActivateAccount_Flow_ActivatesUser() {
        AppUser user = await CreateUserAsync("authactivate", nameof(Roles.Cliente), active: false);

        string rawToken;
        await using (var scope = Fixture.Services.CreateAsyncScope()) {
            var tokenService = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();
            rawToken = await tokenService.GenerateAsync(user.Id, AccountTokenType.Activation);
        }

        await using (var scope = Fixture.Services.CreateAsyncScope()) {
            var handler = new ActivateAccountCommandHandler(
                scope.ServiceProvider.GetRequiredService<IAccountTokenService>()
            );

            var result = await handler.Handle(
                new ActivateAccountCommand(rawToken),
                CancellationToken.None
            );

            result.IsSuccess.Should().BeTrue();
        }

        await using (var scope = Fixture.Services.CreateAsyncScope()) {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            AppUser? activated = await userManager.FindByIdAsync(user.Id);
            activated!.Active.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ActivateAccount_SameTokenTwice_SecondFails() {
        AppUser user = await CreateUserAsync("authtwice", nameof(Roles.Cliente), active: false);

        string rawToken;
        await using (var scope = Fixture.Services.CreateAsyncScope()) {
            var tokenService = scope.ServiceProvider.GetRequiredService<IAccountTokenService>();
            rawToken = await tokenService.GenerateAsync(user.Id, AccountTokenType.Activation);
        }

        await using (var scope = Fixture.Services.CreateAsyncScope()) {
            var handler = new ActivateAccountCommandHandler(
                scope.ServiceProvider.GetRequiredService<IAccountTokenService>()
            );

            (await handler.Handle(new ActivateAccountCommand(rawToken), CancellationToken.None))
                .IsSuccess.Should()
                .BeTrue();

            var second = await handler.Handle(
                new ActivateAccountCommand(rawToken),
                CancellationToken.None
            );
            second.IsFailure.Should().BeTrue();
            second.Error!.Code.Should().Be("Account.InvalidActivationToken");
        }
    }

    [Fact]
    public async Task ResetPassword_FullFlow_ChangesPasswordAndReactivates() {
        AppUser user = await CreateUserAsync("authreset", nameof(Roles.Cliente), active: true);

        var emailService = new EmailCaptureService();
        await using var provider = Fixture.BuildProvider(configure: services =>
            services.AddScoped<IEmailService>(_ => emailService)
        );

        // Solicitar restablecimiento (API: token directo, sin callback)
        string rawToken;
        await using (var scope = provider.CreateAsyncScope()) {
            var handler = new RequestPasswordResetCommandHandler(
                scope.ServiceProvider.GetRequiredService<IUserAccountService>(),
                scope.ServiceProvider.GetRequiredService<IAccountTokenService>(),
                scope.ServiceProvider.GetRequiredService<IEmailService>(),
                Microsoft
                    .Extensions
                    .Logging
                    .Abstractions
                    .NullLogger<RequestPasswordResetCommandHandler>
                    .Instance
            );

            var requestResult = await handler.Handle(
                new RequestPasswordResetCommand("authreset", RoleSets.Mvc, CallbackUrl: null),
                CancellationToken.None
            );

            requestResult.IsSuccess.Should().BeTrue();
            var tokenModel = Assert.IsType<PasswordResetTokenModel>(emailService.LastModel);
            rawToken = tokenModel.Token;
        }

        // El usuario quedó desactivado temporalmente
        await using (var verifyScope = Fixture.Services.CreateAsyncScope()) {
            var userManager = verifyScope.ServiceProvider.GetRequiredService<
                UserManager<AppUser>
            >();
            AppUser? deactivated = await userManager.FindByIdAsync(user.Id);
            deactivated!.Active.Should().BeFalse();
        }

        // Completar el restablecimiento con el token enviado por correo
        await using (var scope = provider.CreateAsyncScope()) {
            var handler = new ResetPasswordCommandHandler(
                scope.ServiceProvider.GetRequiredService<IAccountTokenService>()
            );

            var resetResult = await handler.Handle(
                new ResetPasswordCommand(user.Id, rawToken, "NuevaP@ssw0rd!", "NuevaP@ssw0rd!"),
                CancellationToken.None
            );

            resetResult.IsSuccess.Should().BeTrue();
        }

        // Verificar: nuevo password funciona y usuario reactivado
        await using (var scope = Fixture.Services.CreateAsyncScope()) {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            AppUser? updated = await userManager.FindByIdAsync(user.Id);
            updated!.Active.Should().BeTrue();
            (await userManager.CheckPasswordAsync(updated, "NuevaP@ssw0rd!")).Should().BeTrue();
            (await userManager.CheckPasswordAsync(updated, "P@ssw0rd123!")).Should().BeFalse();
        }
    }
}
