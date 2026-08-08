using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Infrastructure.Identity;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class SeedTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private static readonly IReadOnlyDictionary<string, string?> SeedConfiguration =
        new Dictionary<string, string?> {
            ["Security:DefaultUsers:Admin:UserName"] = "admin",
            ["Security:DefaultUsers:Admin:Password"] = "Admin123!",
            ["Security:DefaultUsers:Admin:FirstName"] = "Admin",
            ["Security:DefaultUsers:Admin:LastName"] = "Sistema",
            ["Security:DefaultUsers:Admin:Email"] = "admin@artemis.local",
            ["Security:DefaultUsers:Admin:IdentityDocument"] = "00100000001",
            ["Security:DefaultUsers:Cashier:UserName"] = "cajero",
            ["Security:DefaultUsers:Cashier:Password"] = "Cajero123!",
            ["Security:DefaultUsers:Cashier:FirstName"] = "Cajero",
            ["Security:DefaultUsers:Cashier:LastName"] = "Operaciones",
            ["Security:DefaultUsers:Cashier:Email"] = "cajero@artemis.local",
            ["Security:DefaultUsers:Cashier:IdentityDocument"] = "00100000002",
            ["Security:DefaultUsers:Client:UserName"] = "cliente01",
            ["Security:DefaultUsers:Client:Password"] = "Cliente123!",
            ["Security:DefaultUsers:Client:FirstName"] = "Cliente",
            ["Security:DefaultUsers:Client:LastName"] = "Principal",
            ["Security:DefaultUsers:Client:Email"] = "cliente@artemis.local",
            ["Security:DefaultUsers:Client:IdentityDocument"] = "00100000003",
            ["Security:DefaultUsers:Commerce:UserName"] = "comercio01",
            ["Security:DefaultUsers:Commerce:Password"] = "Comercio123!",
            ["Security:DefaultUsers:Commerce:FirstName"] = "Comercio",
            ["Security:DefaultUsers:Commerce:LastName"] = "Demo",
            ["Security:DefaultUsers:Commerce:Email"] = "comercio@artemis.local",
            ["Security:DefaultUsers:Commerce:IdentityDocument"] = "00100000004",
        };

    [Fact]
    public async Task SeedAsync_CreatesAllRolesAndActiveUsersOnce() {
        await using var provider = BuildProvider(extraConfiguration: SeedConfiguration);

        await provider.RunIdentitySeedAsync();
        await provider.RunIdentitySeedAsync(); // idempotente

        await using var scope = provider.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();

        foreach (string role in RoleSets.All) {
            (await roleManager.RoleExistsAsync(role)).Should().BeTrue(role);
        }

        List<AppUser> users = await context.Users.OrderBy(user => user.UserName).ToListAsync();
        users.Should().HaveCount(4);
        users.All(user => user.Active).Should().BeTrue();

        users.Select(user => user.UserName).Should().BeEquivalentTo(
            "admin",
            "cajero",
            "cliente01",
            "comercio01"
        );

        foreach (AppUser user in users) {
            IList<string> roles = await scope.ServiceProvider
                .GetRequiredService<UserManager<AppUser>>()
                .GetRolesAsync(user);
            roles.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task SeedAsync_MissingConfigurationForRole_WarnsAndSkips() {
        await using var provider = BuildProvider(extraConfiguration: SeedConfiguration);

        // Sin la sección de usuario comercio: solo se crean los demás.
        await using var partialProvider = BuildProvider(
            extraConfiguration: SeedConfiguration.Where(pair => !pair.Key.Contains("Commerce"))
                .ToDictionary(pair => pair.Key, pair => pair.Value)
        );

        await partialProvider.RunIdentitySeedAsync();

        await using var scope = partialProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();
        (await context.Users.CountAsync()).Should().Be(3);
    }
}
