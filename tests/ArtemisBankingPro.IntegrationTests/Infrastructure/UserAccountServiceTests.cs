using System.Security.Claims;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Identity.Services;
using Microsoft.AspNetCore.Identity;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class UserAccountServiceTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
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
            IdentityDocument = $"0000{userName.Length}0001",
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();

        return user;
    }

    [Fact]
    public async Task ValidateCredentials_ValidUser_ReturnsSuccessWithRole() {
        await CreateUserAsync("admin01", nameof(Roles.Administrador));

        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserAccountService>();

        LoginResult result = await service.ValidateCredentialsAsync(
            "admin01",
            "P@ssw0rd123!",
            RoleSets.Api
        );

        result.IsSuccess.Should().BeTrue();
        result.Role.Should().Be(nameof(Roles.Administrador));
        result.UserId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ValidateCredentials_WrongPassword_ReturnsInvalidCredentials() {
        await CreateUserAsync("admin01", nameof(Roles.Administrador));

        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserAccountService>();

        LoginResult result = await service.ValidateCredentialsAsync(
            "admin01",
            "WrongPass123!",
            RoleSets.Api
        );

        result.Status.Should().Be(LoginStatus.InvalidCredentials);
        result.UserId.Should().BeNull();
    }

    [Fact]
    public async Task ValidateCredentials_UnknownUser_ReturnsInvalidCredentials() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserAccountService>();

        LoginResult result = await service.ValidateCredentialsAsync(
            "no-existe",
            "P@ssw0rd123!",
            RoleSets.Api
        );

        result.Status.Should().Be(LoginStatus.InvalidCredentials);
    }

    [Fact]
    public async Task ValidateCredentials_InactiveUser_ReturnsInactive() {
        AppUser user = await CreateUserAsync("cliente01", nameof(Roles.Cliente), active: false);

        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserAccountService>();

        LoginResult result = await service.ValidateCredentialsAsync(
            "cliente01",
            "P@ssw0rd123!",
            RoleSets.Mvc
        );

        result.Status.Should().Be(LoginStatus.Inactive);
        result.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task ValidateCredentials_RoleNotAllowed_ReturnsRoleNotAllowed() {
        await CreateUserAsync("comercio01", nameof(Roles.Comercio));

        await using var scope = Fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserAccountService>();

        // Comercio no puede usar la WebApp (roles MVC).
        LoginResult mvcResult = await service.ValidateCredentialsAsync(
            "comercio01",
            "P@ssw0rd123!",
            RoleSets.Mvc
        );
        mvcResult.Status.Should().Be(LoginStatus.RoleNotAllowed);

        // Cliente no puede usar la API (roles API).
        await CreateUserAsync("cliente02", nameof(Roles.Cliente));
        LoginResult apiResult = await service.ValidateCredentialsAsync(
            "cliente02",
            "P@ssw0rd123!",
            RoleSets.Api
        );
        apiResult.Status.Should().Be(LoginStatus.RoleNotAllowed);
    }
}

[Collection("SqlServer")]
public sealed class CurrentUserServiceTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private static ClaimsPrincipal CreatePrincipal(
        string? userId,
        string? name,
        string? role,
        string? commerceId = null
    ) {
        var claims = new List<Claim>();
        if (userId is not null) {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        }

        if (name is not null) {
            claims.Add(new Claim(ClaimTypes.Name, name));
        }

        if (role is not null) {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        if (commerceId is not null) {
            claims.Add(new Claim(CurrentUserService.CommerceIdClaim, commerceId));
        }

        var identity = new ClaimsIdentity(claims, "Test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void ReadsClaimsFromPrincipal() {
        var principal = CreatePrincipal("user-1", "comercio", nameof(Roles.Comercio), "7");

        var service = new CurrentUserService(new Microsoft.AspNetCore.Http.HttpContextAccessor {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = principal },
        });

        service.IsAuthenticated.Should().BeTrue();
        service.UserId.Should().Be("user-1");
        service.UserName.Should().Be("comercio");
        service.Role.Should().Be(nameof(Roles.Comercio));
        service.CommerceId.Should().Be(7);
    }

    [Fact]
    public void WithoutContext_ReportsUnauthenticated() {
        var service = new CurrentUserService(new Microsoft.AspNetCore.Http.HttpContextAccessor());

        service.IsAuthenticated.Should().BeFalse();
        service.UserId.Should().BeNull();
        service.Role.Should().BeNull();
        service.CommerceId.Should().BeNull();
    }
}
