using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class UserRepositoryTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private async Task<AppUser> CreateUserAsync(
        string userName,
        string role,
        string document,
        bool active = true
    ) {
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
            IdentityDocument = document,
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        IdentityResult result = await userManager.CreateAsync(user, "P@ssw0rd123!");
        result.Succeeded.Should().BeTrue();

        IdentityResult roleResult = await userManager.AddToRoleAsync(user, role);
        roleResult.Succeeded.Should().BeTrue();

        return user;
    }

    [Fact]
    public async Task GetByUserName_ReturnsDtoWithRole() {
        await CreateUserAsync("cliente01", nameof(Roles.Cliente), "001000001");

        await using var scope = Fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        UserListDto? dto = await repository.GetByUserNameAsync("cliente01");
        Assert.NotNull(dto);
        dto.UserName.Should().Be("cliente01");
        dto.Role.Should().Be(nameof(Roles.Cliente));
        dto.Identification.Should().Be("001000001");
        dto.IsActive.Should().BeTrue();

        UserListDto? missing = await repository.GetByUserNameAsync("no-existe");
        Assert.Null(missing);
    }

    [Fact]
    public async Task Exists_ChecksUserNameEmailAndDocument() {
        await CreateUserAsync("cajero01", nameof(Roles.Cajero), "001000002");

        await using var scope = Fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        (await repository.ExistsByUserNameAsync("cajero01")).Should().BeTrue();
        (await repository.ExistsByUserNameAsync("Cajero01")).Should().BeTrue(); // case-insensitive
        (await repository.ExistsByUserNameAsync("otro")).Should().BeFalse();
        (await repository.ExistsByEmailAsync("cajero01@example.com")).Should().BeTrue();
        (await repository.ExistsByEmailAsync("otro@example.com")).Should().BeFalse();
        (await repository.ExistsByIdentityDocumentAsync("001000002")).Should().BeTrue();
        (await repository.ExistsByIdentityDocumentAsync("999999999")).Should().BeFalse();
    }

    [Fact]
    public async Task GetPagedAsync_ExcludesCommerceAndFiltersByRole() {
        await CreateUserAsync("admin01", nameof(Roles.Administrador), "001000010");
        await CreateUserAsync("cajero01", nameof(Roles.Cajero), "001000011");
        await CreateUserAsync("cliente01", nameof(Roles.Cliente), "001000012");
        await CreateUserAsync("comercio01", nameof(Roles.Comercio), "001000013");

        await using var scope = Fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        PageResult<UserListDto> all = await repository.GetPagedAsync(null, new PageRequest());
        all.TotalCount.Should().Be(3);
        all.Items.Should().NotContain(item => item.Role == nameof(Roles.Comercio));

        PageResult<UserListDto> clients = await repository.GetPagedAsync(
            nameof(Roles.Cliente),
            new PageRequest()
        );
        clients.TotalCount.Should().Be(1);
        clients.Items.Single().UserName.Should().Be("cliente01");
    }

    [Fact]
    public async Task GetCommerceUsersPagedAsync_ReturnsOnlyCommerceUsers() {
        await CreateUserAsync("admin01", nameof(Roles.Administrador), "001000020");
        await CreateUserAsync("comercio01", nameof(Roles.Comercio), "001000021");
        await CreateUserAsync("comercio02", nameof(Roles.Comercio), "001000022");

        await using var scope = Fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        PageResult<UserListDto> commerce = await repository.GetCommerceUsersPagedAsync(
            new PageRequest()
        );
        commerce.TotalCount.Should().Be(2);
        commerce.Items.All(item => item.Role == nameof(Roles.Comercio)).Should().BeTrue();
    }

    [Fact]
    public async Task GetRolesAsync_ReturnsAssignedRoles() {
        AppUser user = await CreateUserAsync("cliente02", nameof(Roles.Cliente), "001000030");

        await using var scope = Fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        IReadOnlyList<string> roles = await repository.GetRolesAsync(user.Id);
        roles.Should().ContainSingle().Which.Should().Be(nameof(Roles.Cliente));
    }
}
