extern alias WebApp;

using System.Reflection;
using System.Security.Claims;
using System.Net;
using ArtemisBankingPro.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using WebApp::ArtemisBankingPro.WebApp.Controllers;
using WebApp::ArtemisBankingPro.WebApp.Navigation;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class WebAppShellTests {
    [Theory]
    [InlineData(nameof(Roles.Administrador), "Administrator")]
    [InlineData(nameof(Roles.Cajero), "Cashier")]
    [InlineData(nameof(Roles.Cliente), "Client")]
    public void Home_index_redirects_to_the_role_specific_home(string role, string action) {
        HomeController controller = ControllerFor(role);

        IActionResult result = controller.Index();

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(action, redirect.ActionName);
    }

    [Fact]
    public void Commerce_has_no_MVC_navigation_and_cannot_resolve_a_home() {
        Assert.Empty(NavigationCatalog.ForRole(nameof(Roles.Comercio)));

        Assert.False(NavigationCatalog.TryGetHome(nameof(Roles.Comercio), out NavigationItem? home));
        Assert.Null(home);

        Assert.IsType<ForbidResult>(ControllerFor(nameof(Roles.Comercio)).Index());
    }

    [Fact]
    public void Navigation_keys_are_scoped_to_the_authenticated_role() {
        Assert.False(NavigationCatalog.TryGetForRole(
                nameof(Roles.Cliente),
                NavigationKeys.AdministratorUsers,
                out NavigationItem? crossRoleItem
            ));
        Assert.Null(crossRoleItem);

        IActionResult result = ControllerFor(nameof(Roles.Cliente))
            .ComingSoon(NavigationKeys.AdministratorUsers);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Client_navigation_exposes_beneficiary_transactions_as_an_implemented_route() {
        NavigationItem item = NavigationCatalog
            .ForRole(nameof(Roles.Cliente))
            .Single(candidate => candidate.Key == NavigationKeys.ClientBeneficiaryTransfer);

        Assert.Equal("Transacciones - Beneficiarios", item.Label);
        Assert.Equal("Client", item.Controller);
        Assert.Equal("BeneficiaryTransfer", item.Action);
        Assert.True(item.IsImplemented);
    }

    [Theory]
    [InlineData(typeof(LoansController), "admin/loans")]
    [InlineData(typeof(CreditCardsController), "admin/credit-cards")]
    [InlineData(typeof(SavingsAccountsController), "admin/savings-accounts")]
    public void Product_controllers_declare_admin_authorization_and_server_routes(
        Type controllerType,
        string route
    ) {
        AuthorizeAttribute authorization = controllerType
            .GetCustomAttribute<AuthorizeAttribute>()!;
        RouteAttribute routeAttribute = controllerType
            .GetCustomAttribute<RouteAttribute>()!;

        Assert.Equal(nameof(Roles.Administrador), authorization.Roles);
        Assert.Equal(route, routeAttribute.Template);
    }

    [Fact]
    public void Cashier_controller_is_restricted_to_cajero_and_uses_a_server_route() {
        AuthorizeAttribute authorization = typeof(CashierController)
            .GetCustomAttribute<AuthorizeAttribute>()!;
        RouteAttribute route = typeof(CashierController)
            .GetCustomAttribute<RouteAttribute>()!;

        Assert.Equal(nameof(Roles.Cajero), authorization.Roles);
        Assert.Equal("cashier", route.Template);
    }

    [Theory]
    [InlineData(nameof(CashierController.Deposit), "deposit")]
    [InlineData(nameof(CashierController.Withdrawal), "withdrawal")]
    [InlineData(nameof(CashierController.CardPayment), "card-payment")]
    [InlineData(nameof(CashierController.LoanPayment), "loan-payment")]
    [InlineData(nameof(CashierController.ThirdPartyTransfer), "third-party-transfer")]
    [InlineData(nameof(CashierController.Operations), "operations")]
    public void Cashier_navigation_actions_have_stable_get_routes(string actionName, string route) {
        MethodInfo action = typeof(CashierController)
            .GetMethods()
            .Single(method => method.Name == actionName
                && method.GetCustomAttributes<HttpGetAttribute>().Any());
        HttpGetAttribute get = action.GetCustomAttributes<HttpGetAttribute>().Single();

        Assert.Equal(route, get.Template);
    }

    [Theory]
    [InlineData(nameof(HomeController.Administrator), nameof(Roles.Administrador))]
    [InlineData(nameof(HomeController.Cashier), nameof(Roles.Cajero))]
    [InlineData(nameof(HomeController.Client), nameof(Roles.Cliente))]
    public void Role_home_actions_declare_server_side_role_authorization(
        string actionName,
        string expectedRole
    ) {
        MethodInfo action = typeof(HomeController).GetMethod(actionName)!;
        AuthorizeAttribute authorization = action
            .GetCustomAttributes<AuthorizeAttribute>()
            .Single();

        Assert.Equal(expectedRole, authorization.Roles);
    }

    [Fact]
    public void Logout_is_a_post_authorized_action_and_access_denied_is_anonymous() {
        MethodInfo logout = typeof(AuthController).GetMethod(nameof(AuthController.Logout))!;
        Assert.NotNull(logout.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal(
            "Administrador,Cajero,Cliente",
            logout.GetCustomAttribute<AuthorizeAttribute>()!.Roles
        );

        MethodInfo accessDenied = typeof(AuthController)
            .GetMethod(nameof(AuthController.AccessDenied))!;
        Assert.NotNull(accessDenied.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task Anonymous_home_is_challenged_to_the_configured_login_path() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using HttpResponseMessage response = await client.GetAsync("/Home/Index");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Auth/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Login_page_renders_the_contract_fields_and_antiforgery_form() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost"),
        });

        using HttpResponseMessage response = await client.GetAsync("/Auth/Login");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"UserName\"", body, StringComparison.Ordinal);
        Assert.Contains("name=\"Password\"", body, StringComparison.Ordinal);
        Assert.Contains("Restablecer contraseña", body, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", body, StringComparison.Ordinal);
        Assert.DoesNotContain("RealEstateApp", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Identity", body, StringComparison.Ordinal);
        Assert.DoesNotContain("MVC", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_challenge_shows_the_contract_access_message_without_using_return_url() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost"),
        });

        using HttpResponseMessage response = await client.GetAsync(
            "/Auth/Login?ReturnUrl=%2FHome%2FAdministrator"
        );
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No tiene permiso para acceder a esta sección.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ReturnUrl", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_access_denied_page_does_not_claim_an_active_session() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost"),
        });

        using HttpResponseMessage response = await client.GetAsync("/Auth/AccessDenied");
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Inicie sesión para acceder a las secciones protegidas.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Su sesión permanece activa", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Authentication_routes_are_public_gets_and_protected_posts() {
        static MethodInfo Action(string name, string verb) => typeof(AuthController)
            .GetMethods()
            .Single(method => method.Name == name && method.GetCustomAttributes<HttpMethodAttribute>()
                .Any(attribute => attribute.HttpMethods.Contains(verb, StringComparer.OrdinalIgnoreCase)));

        foreach (string action in new[] { "Login", "Activate", "RequestPasswordReset", "ResetPassword" }) {
            MethodInfo get = Action(action, "GET");
            MethodInfo post = Action(action, "POST");

            Assert.NotNull(get.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.NotNull(post.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.NotNull(post.GetCustomAttribute<HttpPostAttribute>());
            Assert.Equal("auth", post.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        }
    }

    [Fact]
    public async Task Authentication_posts_are_rate_limited_without_rewriting_the_rejection() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        for (int attempt = 0; attempt < 20; attempt++) {
            using HttpResponseMessage response = await client.PostAsync(
                "/Auth/Login",
                new FormUrlEncodedContent([])
            );
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using HttpResponseMessage rejected = await client.PostAsync(
            "/Auth/Login",
            new FormUrlEncodedContent([])
        );

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(1), rejected.Headers.RetryAfter?.Delta);
        Assert.True(rejected.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("no-store", rejected.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Unknown_routes_render_the_safe_not_found_page_with_security_headers() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost"),
        });

        using HttpResponseMessage response = await client.GetAsync("/not-a-real-route");
        string body = await response.Content.ReadAsStringAsync();
        string renderedText = WebUtility.HtmlDecode(body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("La página no está disponible", renderedText, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Public_shell_uses_versioned_assets_and_a_strict_browser_policy() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost"),
        });

        using HttpResponseMessage response = await client.GetAsync("/Auth/Login");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        int bootstrapIndex = body.IndexOf(
            "/js/theme-bootstrap.js?v=",
            StringComparison.Ordinal
        );
        int stylesheetIndex = body.IndexOf(
            "/css/site.css?v=",
            StringComparison.Ordinal
        );
        Assert.True(bootstrapIndex >= 0);
        Assert.True(stylesheetIndex > bootstrapIndex);
        foreach (string asset in new[] {
            "/favicon.svg?v=",
            "/css/site.css?v=",
            "/js/theme-bootstrap.js?v=",
            "/js/theme.js?v=",
            "/js/site.js?v=",
            "/js/auth.js?v=",
            "/js/dialogs.js?v=",
            "/js/feedback.js?v=",
            "/js/forms.js?v=",
            "/js/user-form.js?v=",
        }) {
            Assert.Contains(asset, body, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("RealEstateApp", body, StringComparison.Ordinal);

        string csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("style-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("worker-src 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Anonymous_protected_redirects_are_not_cacheable() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using HttpResponseMessage response = await client.GetAsync("/cashier");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-cache", response.Headers.Pragma.Single().Name);
    }

    [Fact]
    public async Task Versioned_static_assets_keep_the_framework_cache_contract() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost"),
        });

        using HttpResponseMessage response = await client.GetAsync("/css/site.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual("no-store", response.Headers.CacheControl?.ToString());
    }

    private static HomeController ControllerFor(string role) {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "shell-user-id"),
                new Claim(ClaimTypes.Name, "shell-user"),
                new Claim(ClaimTypes.Role, role),
            ],
            "Test");
        var controller = new HomeController {
            ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext {
                    User = new ClaimsPrincipal(identity),
                },
            },
        };
        return controller;
    }

    private static WebApplicationFactory<WebApp::Program> CreateFactory() {
        string keyRingPath = Path.Combine(
            Path.GetTempPath(),
            "artemis-webapp-shell-tests",
            Guid.NewGuid().ToString("N")
        );

        return new WebApplicationFactory<WebApp::Program>().WithWebHostBuilder(builder => {
            builder.UseEnvironment("Testing");
            builder.UseSetting(
                "ConnectionStrings:ArtemisDb",
                "Server=localhost;Database=ArtemisShellTests;User Id=sa;Password=not-used;TrustServerCertificate=True"
            );
            builder.UseSetting("Security:DataProtection:KeyRingPath", keyRingPath);
            builder.UseSetting("Database:Initialization:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("Database:Initialization:SeedIdentityOnStartup", "false");
        });
    }
}
