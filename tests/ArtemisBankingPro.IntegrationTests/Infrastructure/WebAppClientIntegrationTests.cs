extern alias WebApp;

using System.Net;
using System.Text.RegularExpressions;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class WebAppClientIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string Password = "P@ssw0rd123!";

    [Fact]
    public async Task Client_home_renders_the_active_products_empty_state() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("client-home");
        await CreateUserAsync(factory, userName, "Cliente");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.GetAsync("/Home/Client");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Centro financiero personal", body, StringComparison.Ordinal);
        Assert.Contains("No posee productos financieros activos.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Su espacio de trabajo está listo", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_beneficiaries_renders_the_owned_empty_state_and_antiforgery_form() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("client-beneficiaries");
        await CreateUserAsync(factory, userName, "Cliente");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.GetAsync("/client/beneficiaries?add=true");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No tiene beneficiarios registrados.", body, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", body, StringComparison.Ordinal);
        Assert.Contains("AddForm.SubmissionToken", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_beneficiary_confirmation_uses_a_persistable_operation_type() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("client-confirmation");
        await CreateUserAsync(factory, userName, "Cliente");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        string page = await GetBodyAsync(client, "/client/beneficiaries?add=true");
        string antiforgery = ExtractInput(page, "__RequestVerificationToken");
        string submissionToken = ExtractInput(page, "AddForm.SubmissionToken");

        using HttpResponseMessage response = await client.PostAsync(
            "/client/beneficiaries/add",
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("AddForm.SubmissionToken", submissionToken),
                ("AddForm.DestinationAccountNumber", "000000001")
            )
        );
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Agregar beneficiario", body, StringComparison.Ordinal);
        Assert.Contains("name=\"ConfirmationToken\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_client_cannot_enter_client_routes_by_direct_url() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("client-route");
        await CreateUserAsync(factory, userName, "Administrador");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.GetAsync("/client/transactions");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Auth/AccessDenied", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_confirmation_post_without_antiforgery_is_rejected() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("client-antiforgery");
        await CreateUserAsync(factory, userName, "Cliente");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.PostAsync(
            "/client/transfer-express/confirm",
            Form(("SubmissionToken", "not-a-token"), ("SourceAccountNumber", "000000001"))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Client_transactions_rejects_an_inverted_date_range_inline_before_querying() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("client-date-filter");
        AppUser user = await CreateUserAsync(factory, userName, "Cliente");
        string accountNumber = UniqueAccountNumber();
        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(
                SavingsAccount.OpenPrimary(
                    user.Id,
                    AccountNumber.Create(accountNumber).Value,
                    Money.Zero,
                    "admin",
                    DateTimeOffset.UtcNow
                ).Value
            );
            await context.SaveChangesAsync();
        });

        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.GetAsync(
            "/client/transactions?dateFrom=2026-08-17&dateTo=2026-08-16"
        );
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("La fecha inicial no puede ser posterior a la fecha final.", body, StringComparison.Ordinal);
        Assert.Contains("id=\"DateFrom-error\"", body, StringComparison.Ordinal);
        Assert.Contains("id=\"DateTo-error\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_beneficiaries_shows_list_before_add_form() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("client-beneficiaries");
        await CreateUserAsync(factory, userName, "Cliente");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        string listPage = await GetBodyAsync(client, "/client/beneficiaries");
        string addPage = await GetBodyAsync(client, "/client/beneficiaries?add=true");

        Assert.DoesNotContain("name=\"AddForm.DestinationAccountNumber\"", listPage, StringComparison.Ordinal);
        Assert.Contains("name=\"AddForm.DestinationAccountNumber\"", addPage, StringComparison.Ordinal);
        Assert.Contains("Agregar beneficiario", listPage, StringComparison.Ordinal);
    }

    private static async Task<AppUser> CreateUserAsync(
        WebApplicationFactory<WebApp::Program> factory,
        string userName,
        string role
    ) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            Assert.True((await roleManager.CreateAsync(new IdentityRole(role))).Succeeded);
        }

        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Cliente",
            LastName = "Prueba",
            IdentityDocument = UniqueDocument(),
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>()
            .CreateAsync(user, Password)).Succeeded);
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>()
            .AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private static async Task LoginAsync(HttpClient client, string userName) {
        string page = await GetBodyAsync(client, "/Auth/Login");
        string antiforgery = ExtractInput(page, "__RequestVerificationToken");
        using HttpResponseMessage response = await client.PostAsync(
            "/Auth/Login",
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("UserName", userName),
                ("Password", Password)
            )
        );
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private WebApplicationFactory<WebApp::Program> CreateFactory() {
        string keyRingPath = Path.Combine(
            Path.GetTempPath(),
            "artemis-webapp-client-tests",
            Guid.NewGuid().ToString("N")
        );

        return new WebApplicationFactory<WebApp::Program>().WithWebHostBuilder(builder => {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ArtemisDb", Fixture.ConnectionString);
            builder.UseSetting("Security:DataProtection:KeyRingPath", keyRingPath);
            builder.UseSetting("Security:AccountTokens:PepperKey", TestKeys.TokenPepperKey);
            builder.UseSetting("WebApp:PublicBaseUrl", "https://localhost");
            builder.UseSetting("Database:Initialization:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("Database:Initialization:SeedIdentityOnStartup", "false");
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<WebApp::Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private static async Task<string> GetBodyAsync(HttpClient client, string path) {
        using HttpResponseMessage response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static FormUrlEncodedContent Form(params (string Name, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Name, value.Value)));

    private static string ExtractInput(string body, string name) {
        Match match = Regex.Match(
            body,
            $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
        Assert.True(match.Success, $"Expected input '{name}' in rendered form.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string Unique(string prefix) =>
        $"web-{prefix}-{Guid.NewGuid():N}"[..48];

    private static string UniqueDocument() =>
        Guid.NewGuid().ToString("N")[..11];

    private static string UniqueAccountNumber() =>
        new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).Concat("000000000").Take(9).ToArray());
}
