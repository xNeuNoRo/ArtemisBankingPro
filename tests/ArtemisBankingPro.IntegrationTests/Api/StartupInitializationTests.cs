using System.Net;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Api;

[CollectionDefinition("SeededApi", DisableParallelization = true)]
public sealed class SeededApiCollectionDefinition : ICollectionFixture<SeededApiFactory>;

public sealed class SeededApiFactory : ApiFactory {
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Database:Initialization:SeedIdentityOnStartup", "true");
        builder.UseSetting("Security:DefaultUsers:Admin:UserName", "admin");
        builder.UseSetting("Security:DefaultUsers:Admin:Password", "Admin123!");
        builder.UseSetting("Security:DefaultUsers:Admin:FirstName", "Admin");
        builder.UseSetting("Security:DefaultUsers:Admin:LastName", "Sistema");
        builder.UseSetting("Security:DefaultUsers:Admin:Email", "admin@artemis.local");
        builder.UseSetting("Security:DefaultUsers:Admin:IdentityDocument", "00100000001");
        builder.UseSetting("Security:DefaultUsers:Cashier:UserName", "cajero");
        builder.UseSetting("Security:DefaultUsers:Cashier:Password", "Cajero123!");
        builder.UseSetting("Security:DefaultUsers:Cashier:FirstName", "Cajero");
        builder.UseSetting("Security:DefaultUsers:Cashier:LastName", "Operaciones");
        builder.UseSetting("Security:DefaultUsers:Cashier:Email", "cajero@artemis.local");
        builder.UseSetting("Security:DefaultUsers:Cashier:IdentityDocument", "00100000002");
        builder.UseSetting("Security:DefaultUsers:Client:UserName", "cliente01");
        builder.UseSetting("Security:DefaultUsers:Client:Password", "Cliente123!");
        builder.UseSetting("Security:DefaultUsers:Client:FirstName", "Cliente");
        builder.UseSetting("Security:DefaultUsers:Client:LastName", "Principal");
        builder.UseSetting("Security:DefaultUsers:Client:Email", "cliente@artemis.local");
        builder.UseSetting("Security:DefaultUsers:Client:IdentityDocument", "00100000003");
        builder.UseSetting("Security:DefaultUsers:Commerce:UserName", "comercio01");
        builder.UseSetting("Security:DefaultUsers:Commerce:Password", "Comercio123!");
        builder.UseSetting("Security:DefaultUsers:Commerce:FirstName", "Comercio");
        builder.UseSetting("Security:DefaultUsers:Commerce:LastName", "Demo");
        builder.UseSetting("Security:DefaultUsers:Commerce:Email", "comercio@artemis.local");
        builder.UseSetting("Security:DefaultUsers:Commerce:IdentityDocument", "00100000004");
    }
}

[Collection("SeededApi")]
public sealed class StartupInitializationTests(SeededApiFactory factory) {
    [Fact]
    public async Task Startup_AppliesIdentityMigrationAndSeedsRolesAndActiveUsers() {
        using HttpClient client = factory.CreateClient();
        (await client.GetAsync("/api/users")).StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        await using var scope = factory.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();
        var bankingContext = scope.ServiceProvider.GetRequiredService<BankingDbContext>();

        foreach (string role in RoleSets.All) {
            (await roleManager.RoleExistsAsync(role)).Should().BeTrue(role);
        }

        List<AppUser> users = await context.Users.OrderBy(user => user.UserName).ToListAsync();
        users.Should().HaveCount(4);
        users.Should().OnlyContain(user => user.Active && user.EmailConfirmed);

        foreach (AppUser user in users) {
            (await userManager.GetRolesAsync(user)).Should().ContainSingle();
        }

        (await context.Database.GetAppliedMigrationsAsync())
            .Should().Contain("20260818120000_CommerceEmailUniqueness");
        (await bankingContext.Database.GetAppliedMigrationsAsync())
            .Should().NotBeEmpty();
    }
}
