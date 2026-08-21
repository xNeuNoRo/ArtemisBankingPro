extern alias WebApp;

using System.Net;
using System.Text.RegularExpressions;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class WebAppAuthenticationIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string Password = "P@ssw0rd123!";
    private const string NewPassword = "N3wP@ssword456!";

    [Fact]
    public async Task Login_admin_issues_cookie_redirects_by_role_and_hides_login_when_authenticated() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = UniqueUserName("admin");
        await CreateUserAsync(factory, userName, "Administrador");
        using HttpClient client = CreateClient(factory);

        using HttpResponseMessage loginPage = await client.GetAsync("/Auth/Login");
        string loginBody = await loginPage.Content.ReadAsStringAsync();
        string antiforgery = ExtractInput(loginBody, "__RequestVerificationToken");

        using HttpResponseMessage response = await client.PostAsync(
            "/Auth/Login",
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("UserName", userName),
                ("Password", Password)
            )
        );

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Home/Administrator");
        response.Headers.GetValues("Set-Cookie")
            .Should()
            .Contain(cookie => cookie.Contains(".ArtemisBanking.Auth=", StringComparison.Ordinal));

        using HttpResponseMessage authenticatedLogin = await client.GetAsync("/Auth/Login");

        authenticatedLogin.StatusCode.Should().Be(HttpStatusCode.Redirect);
        authenticatedLogin.Headers.Location!.ToString().Should().Be("/Home/Administrator");
    }

    [Theory]
    [InlineData("Cajero", "/Home/Cashier")]
    [InlineData("Cliente", "/Home/Client")]
    public async Task Login_allowed_roles_redirect_to_their_server_owned_home(
        string role,
        string expectedLocation
    ) {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = UniqueUserName(role.ToLowerInvariant());
        await CreateUserAsync(factory, userName, role);
        using HttpClient client = CreateClient(factory);

        string pageBody = await GetBodyWithAntiforgeryAsync(client, "/Auth/Login");
        string antiforgery = ExtractInput(pageBody, "__RequestVerificationToken");
        using HttpResponseMessage response = await client.PostAsync(
            "/Auth/Login",
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("UserName", userName),
                ("Password", Password)
            )
        );

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(expectedLocation);
    }

    [Fact]
    public async Task Login_rejects_invalid_inactive_and_api_only_users_without_cookie() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string invalidUser = UniqueUserName("invalid");
        string inactiveUser = UniqueUserName("inactive");
        string commerceUser = UniqueUserName("commerce");
        await CreateUserAsync(factory, invalidUser, "Cliente");
        await CreateUserAsync(factory, inactiveUser, "Cliente", active: false);
        await CreateUserAsync(factory, commerceUser, "Comercio");
        using HttpClient client = CreateClient(factory);

        string invalidBody = await PostLoginAndReadBodyAsync(client, invalidUser, "wrong-password");
        invalidBody.Should().Contain("Los datos de acceso son inválidos.");

        string inactiveBody = await PostLoginAndReadBodyAsync(client, inactiveUser, Password);
        inactiveBody.Should().Contain(
            "Su cuenta se encuentra inactiva. Debe activar su cuenta mediante el enlace "
                + "enviado a su correo electrónico registrado para poder acceder al sistema."
        );

        string commerceBody = await PostLoginAndReadBodyAsync(client, commerceUser, Password);
        commerceBody.Should().Contain(
            "Este usuario no tiene permisos para acceder a la aplicación web."
        );
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task Login_post_without_antiforgery_is_rejected() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        using HttpClient client = CreateClient(factory);

        using HttpResponseMessage response = await client.PostAsync(
            "/Auth/Login",
            Form(("UserName", "admin"), ("Password", Password))
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Activation_uses_clean_confirmation_url_consumes_once_and_redirects_to_login() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = UniqueUserName("activation");
        AppUser user = await CreateUserAsync(factory, userName, "Cliente", active: false);
        string rawToken;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope()) {
            rawToken = await scope.ServiceProvider
                .GetRequiredService<IAccountTokenService>()
                .GenerateAsync(user.Id, AccountTokenType.Activation);
        }

        using HttpClient client = CreateClient(factory);
        using HttpResponseMessage linkResponse = await client.GetAsync(
            $"/Auth/Activate?token={Uri.EscapeDataString(rawToken)}"
        );

        linkResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        linkResponse.Headers.Location!.ToString().Should().Be("/Auth/Activate");

        using HttpResponseMessage confirmationPage = await client.GetAsync("/Auth/Activate");
        string confirmationBody = await confirmationPage.Content.ReadAsStringAsync();
        confirmationBody.Contains(rawToken, StringComparison.Ordinal).Should().BeFalse();
        string flow = ExtractInput(confirmationBody, "Token");
        string antiforgery = ExtractInput(confirmationBody, "__RequestVerificationToken");

        using HttpResponseMessage activationResponse = await client.PostAsync(
            "/Auth/Activate",
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("Token", flow)
            )
        );

        activationResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        activationResponse.Headers.Location!.ToString().Should().Be("/Auth/Login");
        (await IsActiveAsync(factory, user.Id)).Should().BeTrue();

        using HttpResponseMessage loginAfterActivation = await client.GetAsync("/Auth/Login");
        string successBody = WebUtility.HtmlDecode(
            await loginAfterActivation.Content.ReadAsStringAsync()
        );
        successBody.Should().Contain("Su cuenta ha sido activada correctamente. Ya puede iniciar sesión.");

        using HttpResponseMessage replayLink = await client.GetAsync(
            $"/Auth/Activate?token={Uri.EscapeDataString(rawToken)}"
        );
        using HttpResponseMessage replayPage = await client.GetAsync(
            replayLink.Headers.Location!.ToString()
        );
        string replayBody = await replayPage.Content.ReadAsStringAsync();
        string replayFlow = ExtractInput(replayBody, "Token");
        string replayAntiforgery = ExtractInput(replayBody, "__RequestVerificationToken");

        using HttpResponseMessage replayResponse = await client.PostAsync(
            "/Auth/Activate",
            Form(
                ("__RequestVerificationToken", replayAntiforgery),
                ("Token", replayFlow)
            )
        );
        string replayResultBody = WebUtility.HtmlDecode(
            await replayResponse.Content.ReadAsStringAsync()
        );

        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        replayResultBody.Should().Contain("Este enlace de activación ya fue utilizado.");
    }

    [Fact]
    public async Task Password_reset_sends_link_resets_password_reactivates_user_and_rejects_replay() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = UniqueUserName("reset");
        AppUser user = await CreateUserAsync(factory, userName, "Cliente");
        using HttpClient client = CreateClient(factory);

        string requestBody = await GetBodyWithAntiforgeryAsync(client, "/Auth/RequestPasswordReset");
        string requestAntiforgery = ExtractInput(requestBody, "__RequestVerificationToken");
        using HttpResponseMessage requestResponse = await client.PostAsync(
            "/Auth/RequestPasswordReset",
            Form(
                ("__RequestVerificationToken", requestAntiforgery),
                ("UserName", userName)
            )
        );

        requestResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        requestResponse.Headers.Location!.ToString().Should().Be("/Auth/Login");
        (await IsActiveAsync(factory, user.Id)).Should().BeFalse();

        RecordingEmailService email = factory.Services.GetRequiredService<RecordingEmailService>();
        PasswordResetModel resetEmail = Assert.IsType<PasswordResetModel>(email.LastModel);
        Uri resetLink = new(resetEmail.ResetLink);

        using HttpResponseMessage resetLinkResponse = await client.GetAsync(resetLink.PathAndQuery);
        resetLinkResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resetLinkResponse.Headers.Location!.ToString().Should().Be("/Auth/ResetPassword");

        using HttpResponseMessage resetPage = await client.GetAsync("/Auth/ResetPassword");
        string resetBody = await resetPage.Content.ReadAsStringAsync();
        resetBody.Contains(resetLink.Query, StringComparison.Ordinal).Should().BeFalse();
        string userFlow = ExtractInput(resetBody, "UserId");
        string tokenFlow = ExtractInput(resetBody, "Token");
        string resetAntiforgery = ExtractInput(resetBody, "__RequestVerificationToken");

        using HttpResponseMessage resetResponse = await client.PostAsync(
            "/Auth/ResetPassword",
            Form(
                ("__RequestVerificationToken", resetAntiforgery),
                ("UserId", userFlow),
                ("Token", tokenFlow),
                ("Password", NewPassword),
                ("ConfirmPassword", NewPassword)
            )
        );

        resetResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resetResponse.Headers.Location!.ToString().Should().Be("/Auth/Login");
        (await IsActiveAsync(factory, user.Id)).Should().BeTrue();

        using HttpResponseMessage loginAfterReset = await client.GetAsync("/Auth/Login");
        string resetSuccessBody = WebUtility.HtmlDecode(
            await loginAfterReset.Content.ReadAsStringAsync()
        );
        resetSuccessBody.Should().Contain(
            "Su contraseña ha sido restablecida correctamente. Ya puede iniciar sesión."
        );

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope()) {
            UserManager<AppUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            AppUser currentUser = (await userManager.FindByIdAsync(user.Id))!;
            (await userManager.CheckPasswordAsync(currentUser, NewPassword)).Should().BeTrue();
        }

        using HttpResponseMessage replayResponse = await client.PostAsync(
            "/Auth/ResetPassword",
            Form(
                ("__RequestVerificationToken", resetAntiforgery),
                ("UserId", userFlow),
                ("Token", tokenFlow),
                ("Password", NewPassword),
                ("ConfirmPassword", NewPassword)
            )
        );
        string replayBody = WebUtility.HtmlDecode(
            await replayResponse.Content.ReadAsStringAsync()
        );

        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        replayBody.Should().Contain("Este enlace de restablecimiento ya fue utilizado.");
    }

    [Fact]
    public async Task Password_reset_unknown_user_returns_contract_message_without_sending_email() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        using HttpClient client = CreateClient(factory);

        string pageBody = await GetBodyWithAntiforgeryAsync(client, "/Auth/RequestPasswordReset");
        string antiforgery = ExtractInput(pageBody, "__RequestVerificationToken");
        using HttpResponseMessage response = await client.PostAsync(
            "/Auth/RequestPasswordReset",
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("UserName", "missing-user")
            )
        );
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("No existe un usuario registrado con este nombre de usuario.");
        Assert.Null(factory.Services.GetRequiredService<RecordingEmailService>().LastModel);
    }

    private static async Task<AppUser> CreateUserAsync(
        WebApplicationFactory<WebApp::Program> factory,
        string userName,
        string role,
        bool active = true
    ) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        UserManager<AppUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Web",
            LastName = "Auth",
            IdentityDocument = $"{(uint)StringComparer.Ordinal.GetHashCode(userName) % 1_000_000_000:D9}",
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return user;
    }

    private static async Task<bool?> IsActiveAsync(
        WebApplicationFactory<WebApp::Program> factory,
        string userId
    ) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>()
            .FindByIdAsync(userId))?.Active;
    }

    private static async Task<string> PostLoginAndReadBodyAsync(
        HttpClient client,
        string userName,
        string password
    ) {
        string pageBody = await GetBodyWithAntiforgeryAsync(client, "/Auth/Login");
        string antiforgery = ExtractInput(pageBody, "__RequestVerificationToken");
        using HttpResponseMessage response = await client.PostAsync(
            "/Auth/Login",
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("UserName", userName),
                ("Password", password)
            )
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> GetBodyWithAntiforgeryAsync(HttpClient client, string path) {
        using HttpResponseMessage response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static HttpClient CreateClient(WebApplicationFactory<WebApp::Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private WebApplicationFactory<WebApp::Program> CreateFactory() {
        string keyRingPath = Path.Combine(
            Path.GetTempPath(),
            "artemis-webapp-auth-tests",
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
            });
        });
    }

    private static FormUrlEncodedContent Form(params (string Name, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Name, value.Value)));

    private static string ExtractInput(string body, string name) {
        Match match = Regex.Match(
            body,
            $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
        match.Success.Should().BeTrue($"Expected input '{name}' in the rendered form.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string UniqueUserName(string prefix) {
        string value = $"web-{prefix}-{Guid.NewGuid():N}";
        return value[..Math.Min(48, value.Length)];
    }

    private sealed class RecordingEmailService : IEmailService {
        public string? Recipient { get; private set; }

        public IEmailModel? LastModel { get; private set; }

        public Task SendAsync<T>(string recipient, T model, CancellationToken ct = default)
            where T : IEmailModel {
            Recipient = recipient;
            LastModel = model;
            return Task.CompletedTask;
        }
    }
}
