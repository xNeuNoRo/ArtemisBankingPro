using System.Data.Common;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Infrastructure.Identity;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class WebAppCookieAuthenticationIntegrationTests(SqlServerFixture fixture) {
    [Fact]
    public async Task Sign_in_and_sign_out_write_the_identity_cookie() {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        UserManager<AppUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        const string role = "Cliente";
        const string userName = "mvc-cookie-client";
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        var user = new AppUser {
            UserName = userName,
            Email = "mvc-cookie-client@example.com",
            FirstName = "MVC",
            LastName = "Client",
            IdentityDocument = "MVC001",
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();

        try {
            var httpContext = new DefaultHttpContext {
                RequestServices = scope.ServiceProvider,
            };
            IHttpContextAccessor accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = httpContext;
            IUserAccountService accountService = scope.ServiceProvider.GetRequiredService<IUserAccountService>();

            Result signIn = await accountService.SignInWebAppAsync(
                user.Id,
                role,
                CancellationToken.None
            );

            signIn.IsSuccess.Should().BeTrue();
            httpContext.Response.Headers.SetCookie.ToString()
                .Should().Contain(".ArtemisBanking.Auth");

            Result signOut = await accountService.SignOutWebAppAsync(CancellationToken.None);

            signOut.IsSuccess.Should().BeTrue();
            httpContext.Response.Headers.SetCookie.ToString()
                .Should().Contain(".ArtemisBanking.Auth");
        }
        finally {
            (await userManager.DeleteAsync(user)).Succeeded.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Commerce_and_inactive_users_cannot_receive_an_mvc_cookie() {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        UserManager<AppUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        AppUser commerce = await CreateUserAsync(userManager, roleManager, "Comercio", "mvc-cookie-commerce", true);
        AppUser inactive = await CreateUserAsync(userManager, roleManager, "Cliente", "mvc-cookie-inactive", false);
        try {
            var httpContext = new DefaultHttpContext {
                RequestServices = scope.ServiceProvider,
            };
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;
            IUserAccountService accountService = scope.ServiceProvider.GetRequiredService<IUserAccountService>();

            Result commerceResult = await accountService.SignInWebAppAsync(
                commerce.Id,
                "Comercio",
                CancellationToken.None
            );
            Result inactiveResult = await accountService.SignInWebAppAsync(
                inactive.Id,
                "Cliente",
                CancellationToken.None
            );

            commerceResult.Error!.Code.Should().Be("Auth.RoleNotAllowed");
            inactiveResult.Error!.Code.Should().Be("Auth.Inactive");
            httpContext.Response.Headers.SetCookie.Should().BeEmpty();
        }
        finally {
            (await userManager.DeleteAsync(commerce)).Succeeded.Should().BeTrue();
            (await userManager.DeleteAsync(inactive)).Succeeded.Should().BeTrue();
        }
    }

    private ServiceProvider BuildProvider() {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:ArtemisDb"] = fixture.ConnectionString,
                ["Security:DataProtection:KeyRingPath"] = Path.Combine(
                    Path.GetTempPath(),
                    "artemis-tests",
                    Guid.NewGuid().ToString("N")
                ),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<DbConnection>(_ => new SqlConnection(fixture.ConnectionString));
        services.AddIdentityForWebApp(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task<AppUser> CreateUserAsync(
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager,
        string role,
        string userName,
        bool active
    ) {
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "MVC",
            LastName = "Test",
            IdentityDocument = role == "Comercio" ? "MVC002" : "MVC003",
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return user;
    }
}
