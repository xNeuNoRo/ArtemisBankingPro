using System.Data.Common;
using ArtemisBankingPro.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class WebAppIdentityConfigurationTests {
    [Fact]
    public void Production_cookie_is_secure_and_has_the_required_mvc_paths() {
        using ServiceProvider provider = BuildProvider("Production");
        CookieAuthenticationOptions options = provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        options.LoginPath.ToString().Should().Be(ServicesRegistration.LoginPath);
        options.AccessDeniedPath.ToString().Should().Be(ServicesRegistration.AccessDeniedPath);
        options.Cookie.HttpOnly.Should().BeTrue();
        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
        options.Cookie.SameSite.Should().Be(SameSiteMode.Lax);
        options.ExpireTimeSpan.Should().Be(TimeSpan.FromHours(8));
        options.SlidingExpiration.Should().BeTrue();
    }

    [Fact]
    public void Development_cookie_can_run_over_local_http_but_keeps_identity_settings() {
        using ServiceProvider provider = BuildProvider(Environments.Development);
        CookieAuthenticationOptions options = provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.SameAsRequest);
        options.Cookie.HttpOnly.Should().BeTrue();
        Assert.NotNull(provider.GetRequiredService<IHttpContextAccessor>());
    }

    [Fact]
    public void NonDevelopment_requires_persistent_data_protection_key_ring() {
        var services = new ServiceCollection();
        services.AddSingleton<DbConnection>(_ => new SqlConnection(
            "Server=localhost;Database=not-used;User Id=sa;Password=not-used;TrustServerCertificate=True"
        ));
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var environment = new HostEnvironment {
            EnvironmentName = Environments.Production,
            ApplicationName = "ArtemisBankingPro.WebApp",
        };

        Action act = () => services.AddIdentityForWebApp(configuration, environment);

        act.Should().Throw<InvalidOperationException>().WithMessage("*KeyRingPath*");
    }

    private static ServiceProvider BuildProvider(string environmentName) {
        var services = new ServiceCollection();
        services.AddSingleton<DbConnection>(_ => new SqlConnection(
            "Server=localhost;Database=not-used;User Id=sa;Password=not-used;TrustServerCertificate=True"
        ));
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:ArtemisDb"] = "Server=localhost;Database=not-used;User Id=sa;Password=not-used;TrustServerCertificate=True",
                ["Security:DataProtection:KeyRingPath"] = Path.Combine(
                    Path.GetTempPath(),
                    "artemis-tests",
                    Guid.NewGuid().ToString("N")
                ),
            })
            .Build();
        var environment = new HostEnvironment {
            EnvironmentName = environmentName,
            ApplicationName = "ArtemisBankingPro.WebApp",
        };

        services.AddIdentityForWebApp(configuration, environment);
        return services.BuildServiceProvider();
    }

    private sealed class HostEnvironment : IHostEnvironment {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
