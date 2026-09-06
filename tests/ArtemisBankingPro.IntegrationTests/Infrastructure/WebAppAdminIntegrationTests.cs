extern alias WebApp;

using System.Net;
using System.Text.RegularExpressions;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Application.Features.Admin.Services;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Services;
using ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels;
using ArtemisBankingPro.Application.Features.Users.ViewModels;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class WebAppAdminIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string Password = "P@ssw0rd123!";

    [Fact]
    public async Task Administrator_dashboard_renders_database_indicators_and_admin_entry_point() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("dashboard-admin");
        await CreateUserAsync(factory, userName, "Administrador");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.GetAsync("/Home/Administrator");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Centro de control", body, StringComparison.Ordinal);
        Assert.Contains("Transacciones históricas", body, StringComparison.Ordinal);
        Assert.Contains("Promedio de deuda por cliente activo", body, StringComparison.Ordinal);
        Assert.Contains("Gestionar usuarios", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Su espacio de trabajo está listo", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Users_list_excludes_commerce_filters_by_role_and_limits_pages_to_twenty() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string admin = Unique("users-admin");
        await CreateUserAsync(factory, admin, "Administrador");
        for (int index = 0; index < 21; index++) {
            await CreateUserAsync(factory, Unique($"client-{index:00}"), "Cliente");
        }

        string commerce = Unique("commerce");
        await CreateUserAsync(factory, commerce, "Comercio");

        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, admin);

        using HttpResponseMessage response = await client.GetAsync(
            "/Admin/Users?role=Cliente&page=1&pageSize=20"
        );
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("21 registros", body, StringComparison.Ordinal);
        Assert.Contains("Página 1 de 2", body, StringComparison.Ordinal);
        Assert.Contains("value=\"Cliente\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain(commerce, body, StringComparison.Ordinal);
        Assert.Equal(
            20,
            Regex.Matches(body, "admin-users__person-name", RegexOptions.CultureInvariant).Count
        );
    }

    [Theory]
    [InlineData("/admin/credit-cards", "No existe un cliente registrado con esta cédula.")]
    [InlineData("/admin/savings-accounts", "No existe un cliente registrado con esta cédula.")]
    [InlineData("/admin/loans", "No existe un cliente registrado con esta cédula.")]
    public async Task Product_lists_show_the_contract_message_for_an_unknown_customer(
        string route,
        string expectedMessage
    ) {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string admin = Unique("unknown-customer");
        await CreateUserAsync(factory, admin, "Administrador");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, admin);

        using HttpResponseMessage response = await client.GetAsync(
            $"{route}?identification=99999999999"
        );
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedMessage, WebUtility.HtmlDecode(body), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/admin/savings-accounts")]
    [InlineData("/admin/loans")]
    [InlineData("/admin/credit-cards")]
    [InlineData("/admin/savings-accounts/assign")]
    [InlineData("/admin/loans/assign")]
    [InlineData("/admin/credit-cards/assign")]
    public async Task Product_pages_return_model_state_for_an_overlong_identification(
        string route
    ) {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string admin = Unique("invalid-identification");
        await CreateUserAsync(factory, admin, "Administrador");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, admin);

        const string invalidIdentification = "123456789012345678901";
        using HttpResponseMessage response = await client.GetAsync(
            $"{route}?identification={invalidIdentification}"
        );
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("La cédula no debe exceder 11 caracteres.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Users_filter_returns_model_state_instead_of_calling_application_with_invalid_values() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string admin = Unique("invalid-users-filter");
        await CreateUserAsync(factory, admin, "Administrador");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, admin);

        using HttpResponseMessage response = await client.GetAsync(
            "/Admin/Users?role=NoExiste&page=0&pageSize=21"
        );
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("El rol debe ser Administrador, Cajero o Cliente.", body, StringComparison.Ordinal);
        Assert.Contains("La página debe ser mayor o igual a 1.", body, StringComparison.Ordinal);
        Assert.Contains("El tamaño de página debe estar entre 1 y 20.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_load_errors_do_not_render_domain_or_identity_details() {
        Mock<IAdminUserService> users = new();
        users.Setup(service => service.GetUsersAsync(
                It.IsAny<UserListViewModel>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Failure<UserListViewModel>(
                DomainError.Conflict("Identity.Internal", "IdentityUser internal details")
            ));

        using WebApplicationFactory<WebApp::Program> factory = CreateFactory(services => {
            services.RemoveAll<IAdminUserService>();
            services.AddSingleton(users.Object);
        });
        string admin = Unique("admin-redaction");
        await CreateUserAsync(factory, admin, "Administrador");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, admin);

        using HttpResponseMessage response = await client.GetAsync("/Admin/Users");
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No fue posible cargar los usuarios.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentityUser internal details", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_product_load_errors_do_not_render_domain_or_identity_details() {
        Mock<ISavingsAccountManagementService> accounts = new();
        accounts.Setup(service => service.GetAccountsAsync(
                It.IsAny<SavingsAccountListViewModel>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Failure<SavingsAccountListViewModel>(
                DomainError.Conflict("Identity.Internal", "IdentityUser internal details")
            ));

        using WebApplicationFactory<WebApp::Program> factory = CreateFactory(services => {
            services.RemoveAll<ISavingsAccountManagementService>();
            services.AddSingleton(accounts.Object);
        });
        string admin = Unique("product-redaction");
        await CreateUserAsync(factory, admin, "Administrador");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, admin);

        using HttpResponseMessage response = await client.GetAsync("/admin/savings-accounts");
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No fue posible cargar las cuentas de ahorro.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentityUser internal details", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_user_keeps_account_inactive_and_reports_activation_email_outcome() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string admin = Unique("create-admin");
        await CreateUserAsync(factory, admin, "Administrador");
        await CreateUserAsync(factory, Unique("role-client"), "Cliente");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, admin);

        string createPage = await GetBodyAsync(client, "/Admin/CreateUser");
        string antiforgery = ExtractInput(createPage, "__RequestVerificationToken");
        string submissionToken = ExtractInput(createPage, "submissionToken");
        string userName = Unique("new-client");

        using HttpResponseMessage response = await client.PostAsync(
            "/Admin/CreateUser",
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("submissionToken", submissionToken),
                ("FirstName", "Nuevo"),
                ("LastName", "Cliente"),
                ("Identification", UniqueDocument()),
                ("Email", $"{userName}@example.com"),
                ("UserName", userName),
                ("Password", Password),
                ("ConfirmPassword", Password),
                ("Role", "Cliente"),
                ("InitialAmount", "125")
            )
        );
        string createResponseBody = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Redirect, createResponseBody);
        Assert.Equal("/Admin/Users", response.Headers.Location?.ToString());

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        UserManager<AppUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        AppUser? created = await userManager.FindByNameAsync(userName);
        Assert.NotNull(created);
        Assert.False(created.Active);

        RecordingEmailService email = factory.Services.GetRequiredService<RecordingEmailService>();
        AccountActivationModel activation = Assert.IsType<AccountActivationModel>(email.LastModel);
        Assert.Contains("/Auth/Activate?token=", activation.ActivationLink, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_user_updates_allowed_fields_but_self_edit_is_denied() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string admin = Unique("edit-admin");
        string target = Unique("edit-target");
        await CreateUserAsync(factory, admin, "Administrador");
        AppUser targetUser = await CreateUserAsync(factory, target, "Cajero");
        AppUser adminUser = await FindUserAsync(factory, admin);
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, admin);

        using HttpResponseMessage selfResponse = await client.GetAsync(
            $"/Admin/EditUser/{Uri.EscapeDataString(adminUser.Id)}"
        );
        string selfBody = await selfResponse.Content.ReadAsStringAsync();
        Assert.True(
            selfResponse.StatusCode == HttpStatusCode.Redirect,
            $"status={selfResponse.StatusCode}; location={selfResponse.Headers.Location}; body={selfBody}"
        );
        Assert.Equal("/Auth/AccessDenied", selfResponse.Headers.Location?.ToString());

        string editPage = await GetBodyAsync(
            client,
            $"/Admin/EditUser/{Uri.EscapeDataString(targetUser.Id)}"
        );
        string antiforgery = ExtractInput(editPage, "__RequestVerificationToken");
        string submissionToken = ExtractInput(editPage, "submissionToken");
        string updatedEmail = $"{target}@updated.example.com";

        using HttpResponseMessage response = await client.PostAsync(
            $"/Admin/EditUser/{Uri.EscapeDataString(targetUser.Id)}",
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("submissionToken", submissionToken),
                ("FirstName", "Nombre actualizado"),
                ("LastName", "Cajero"),
                ("Identification", targetUser.IdentityDocument),
                ("Email", updatedEmail),
                ("UserName", target),
                ("Password", ""),
                ("ConfirmPassword", "")
            )
        );

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Users", response.Headers.Location?.ToString());

        AppUser updated = await FindUserAsync(factory, target);
        Assert.Equal("Nombre actualizado", updated.FirstName);
        Assert.Equal(updatedEmail, updated.Email);
    }

    [Fact]
    public async Task Status_confirmation_is_single_use_and_updates_user_once() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string admin = Unique("status-admin");
        string target = Unique("status-target");
        await CreateUserAsync(factory, admin, "Administrador");
        AppUser targetUser = await CreateUserAsync(factory, target, "Cliente", active: false);
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, admin);

        string confirmationPage = await GetBodyAsync(
            client,
            $"/Admin/ConfirmUserStatus/{Uri.EscapeDataString(targetUser.Id)}?targetActive=true"
        );
        Assert.True(
            WebUtility.HtmlDecode(confirmationPage).Contains(
                "¿Está seguro que desea activar este usuario?",
                StringComparison.Ordinal
            ),
            confirmationPage
        );
        string antiforgery = ExtractInput(confirmationPage, "__RequestVerificationToken");
        string confirmationToken = ExtractInput(confirmationPage, "ConfirmationToken");
        string path = $"/Admin/ConfirmUserStatus/{Uri.EscapeDataString(targetUser.Id)}?targetActive=true";

        using HttpResponseMessage response = await client.PostAsync(
            path,
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("ConfirmationToken", confirmationToken)
            )
        );
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Users", response.Headers.Location?.ToString());

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope()) {
            AppUser? current = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>()
                .FindByIdAsync(targetUser.Id);
            Assert.NotNull(current);
            Assert.True(current.Active);
        }

        using HttpResponseMessage replay = await client.PostAsync(
            path,
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("ConfirmationToken", confirmationToken)
            )
        );
        string replayBody = await replay.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Contains(
            "La confirmación ya fue utilizada",
            WebUtility.HtmlDecode(replayBody),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Non_admin_cannot_enter_admin_users_by_direct_url() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string clientUser = Unique("direct-client");
        await CreateUserAsync(factory, clientUser, "Cliente");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, clientUser);

        using HttpResponseMessage response = await client.GetAsync(
            "/Admin/Users",
            HttpCompletionOption.ResponseHeadersRead
        );

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            "/Auth/AccessDenied",
            response.Headers.Location?.ToString(),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("/admin/loans", "Gestión de préstamos", "Asignar préstamo")]
    [InlineData("/admin/credit-cards", "Gestión de tarjetas de crédito", "Asignar tarjeta")]
    [InlineData("/admin/savings-accounts", "Gestión de cuentas de ahorro", "Asignar cuenta secundaria")]
    public async Task Administrator_product_pages_render_their_server_owned_entry_points(
        string path,
        string title,
        string entryPoint
    ) {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string admin = Unique("products-admin");
        await CreateUserAsync(factory, admin, "Administrador");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, admin);

        using HttpResponseMessage response = await client.GetAsync(path);
        string body = await response.Content.ReadAsStringAsync();
        string renderedBody = WebUtility.HtmlDecode(body);

        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        Assert.Contains(title, renderedBody, StringComparison.Ordinal);
        Assert.Contains(entryPoint, renderedBody, StringComparison.Ordinal);
        Assert.Contains("Administración / Productos", renderedBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/admin/loans")]
    [InlineData("/admin/credit-cards")]
    [InlineData("/admin/savings-accounts")]
    public async Task Non_admin_cannot_enter_product_modules_by_direct_url(string path) {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string clientUser = Unique("products-client");
        await CreateUserAsync(factory, clientUser, "Cliente");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, clientUser);

        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            "/Auth/AccessDenied",
            response.Headers.Location?.ToString(),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("/admin/loans/assign", "Seleccionar cliente para el préstamo")]
    [InlineData("/admin/credit-cards/assign", "Seleccionar cliente para la tarjeta")]
    [InlineData("/admin/savings-accounts/assign", "Seleccionar cliente para la cuenta secundaria")]
    public async Task Administrator_assignment_pages_render_the_empty_selection_state(
        string path,
        string title
    ) {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string admin = Unique("assignment-admin");
        await CreateUserAsync(factory, admin, "Administrador");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, admin);

        using HttpResponseMessage response = await client.GetAsync(path);
        string body = await response.Content.ReadAsStringAsync();
        string renderedBody = WebUtility.HtmlDecode(body);

        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        Assert.Contains(title, renderedBody, StringComparison.Ordinal);
        Assert.Contains("No hay clientes elegibles", renderedBody, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", renderedBody, StringComparison.Ordinal);
    }

    private static async Task<AppUser> CreateUserAsync(
        WebApplicationFactory<WebApp::Program> factory,
        string userName,
        string role,
        bool active = true
    ) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            Assert.True((await roleManager.CreateAsync(new IdentityRole(role))).Succeeded);
        }

        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Prueba",
            LastName = role,
            IdentityDocument = UniqueDocument(),
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>()
            .CreateAsync(user, Password)).Succeeded);
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>()
            .AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private static async Task<AppUser> FindUserAsync(
        WebApplicationFactory<WebApp::Program> factory,
        string userName
    ) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppUser? user = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>()
            .FindByNameAsync(userName);
        Assert.NotNull(user);
        return user ?? throw new InvalidOperationException("Expected test user.");
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

    private static async Task<string> GetBodyAsync(HttpClient client, string path) {
        using HttpResponseMessage response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private WebApplicationFactory<WebApp::Program> CreateFactory(
        Action<IServiceCollection>? configure = null
    ) {
        string keyRingPath = Path.Combine(
            Path.GetTempPath(),
            "artemis-webapp-admin-tests",
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
            builder.ConfigureServices(services => {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<RecordingEmailService>();
                services.AddScoped<IEmailService>(provider =>
                    provider.GetRequiredService<RecordingEmailService>()
                );
                configure?.Invoke(services);
            });
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<WebApp::Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private static FormUrlEncodedContent Form(params (string Name, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Name, value.Value)));

    private static string ExtractInput(string body, string name) {
        System.Text.RegularExpressions.Match match = Regex.Match(
            body,
            $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
        Assert.True(match.Success, $"Expected input '{name}' in rendered form.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string Unique(string prefix) =>
        $"web-{prefix}-{Guid.NewGuid():N}"[..Math.Min(48, $"web-{prefix}-{Guid.NewGuid():N}".Length)];

    private static string UniqueDocument() =>
        Guid.NewGuid().ToString("N")[..11];

    private sealed class RecordingEmailService : IEmailService {
        public IEmailModel? LastModel { get; private set; }

        public Task SendAsync<T>(string recipient, T model, CancellationToken ct = default)
            where T : IEmailModel {
            LastModel = model;
            return Task.CompletedTask;
        }
    }
}
