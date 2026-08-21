extern alias WebApp;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class WebAppMvcSecurityConfigurationTests {
    [Fact]
    public void Mvc_writes_require_antiforgery_by_default() {
        using WebApplicationFactory<WebApp::Program> factory =
            new WebApplicationFactory<WebApp::Program>().WithWebHostBuilder(builder => {
                builder.UseEnvironment("Testing");
                builder.UseSetting(
                    "ConnectionStrings:ArtemisDb",
                    "Server=localhost;Database=ArtemisWebAppConfigTests;User Id=sa;Password=not-used;TrustServerCertificate=True"
                );
                builder.UseSetting(
                    "Security:DataProtection:KeyRingPath",
                    Path.Combine(Path.GetTempPath(), "artemis-webapp-tests", Guid.NewGuid().ToString("N"))
                );
                builder.UseSetting("Database:Initialization:ApplyMigrationsOnStartup", "false");
                builder.UseSetting("Database:Initialization:SeedIdentityOnStartup", "false");
            });

        MvcOptions mvcOptions = factory.Services.GetRequiredService<IOptions<MvcOptions>>().Value;

        mvcOptions.Filters.Should().ContainSingle(item =>
            item is AutoValidateAntiforgeryTokenAttribute
        );
    }

}
