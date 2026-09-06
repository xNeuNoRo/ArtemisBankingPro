extern alias WebApp;

using System.Net;
using System.Text.RegularExpressions;
using ArtemisBankingPro.Application.Features.Cashier.Services;
using ArtemisBankingPro.Application.Features.Cashier.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class WebAppCashierIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string Password = "P@ssw0rd123!";

    [Fact]
    public async Task Cashier_home_renders_daily_indicators_and_operation_links() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("cashier-home");
        await CreateUserAsync(factory, userName, "Cajero");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.GetAsync("/Home/Cashier");
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Centro de operaciones de caja", body, StringComparison.Ordinal);
        Assert.Contains("Operaciones del día", body, StringComparison.Ordinal);
        Assert.Contains("/cashier/deposit", body, StringComparison.Ordinal);
        Assert.Contains("/cashier/operations", body, StringComparison.Ordinal);
        Assert.DoesNotContain("está listo para incorporar", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_deposit_renders_form_wrapper_and_antiforgery() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("cashier-deposit");
        await CreateUserAsync(factory, userName, "Cajero");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.GetAsync("/cashier/deposit");
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"Form.AccountNumber\"", body, StringComparison.Ordinal);
        Assert.Contains("name=\"Form.Amount\"", body, StringComparison.Ordinal);
        Assert.Contains("id=\"Form_AccountNumber-error\"", body, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_deposit_validation_associates_the_invalid_control_with_its_message() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("cashier-validation");
        await CreateUserAsync(factory, userName, "Cajero");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        string formBody = await GetBodyAsync(client, "/cashier/deposit");
        string antiforgery = ExtractInput(formBody, "__RequestVerificationToken");
        using HttpResponseMessage response = await client.PostAsync(
            "/cashier/deposit",
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("Form.AccountNumber", "bad"),
                ("Form.Amount", "10.00")
            )
        );
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("aria-invalid=\"true\"", body, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"Form_AccountNumber-error\"", body, StringComparison.Ordinal);
        Assert.Contains("id=\"Form_AccountNumber-error\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_deposit_invalid_amount_keeps_a_spanish_server_message() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("cashier-invalid-amount");
        await CreateUserAsync(factory, userName, "Cajero");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        string page = await GetBodyAsync(client, "/cashier/deposit");
        using HttpResponseMessage response = await client.PostAsync(
            "/cashier/deposit",
            Form(
                ("__RequestVerificationToken", ExtractInput(page, "__RequestVerificationToken")),
                ("Form.AccountNumber", "000000001"),
                ("Form.Amount", "no-es-un-monto")
            )
        );
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("El monto a depositar debe ser mayor que cero.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("The value", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/cashier/deposit", "Depósito", "Realizar depósito")]
    [InlineData("/cashier/withdrawal", "Retiro", "Realizar retiro")]
    [InlineData("/cashier/third-party-transfer", "Transacciones a cuentas de terceros", "Realizar transacción")]
    public async Task Cashier_operation_forms_use_contract_copy_and_server_validation(
        string path,
        string title,
        string submitText
    ) {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("cashier-copy");
        await CreateUserAsync(factory, userName, "Cajero");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.GetAsync(path);
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(title, body, StringComparison.Ordinal);
        Assert.Contains(submitText, body, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_third_party_transfer_shows_the_source_account_message() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("cashier-transfer-source");
        await CreateUserAsync(factory, userName, "Cajero");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        string page = await GetBodyAsync(client, "/cashier/third-party-transfer");
        using HttpResponseMessage response = await client.PostAsync(
            "/cashier/third-party-transfer",
            Form(
                ("__RequestVerificationToken", ExtractInput(page, "__RequestVerificationToken")),
                ("Form.SourceAccountNumber", UniqueAccountNumber()),
                ("Form.DestinationAccountNumber", UniqueAccountNumber()),
                ("Form.Amount", "10.00")
            )
        );
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "El número de cuenta origen ingresado no corresponde a una cuenta válida.",
            body,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Cashier_third_party_transfer_shows_the_destination_account_message() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string cashierName = Unique("cashier-transfer-destination");
        AppUser cashier = await CreateUserAsync(factory, cashierName, "Cajero");
        AppUser owner = await CreateUserAsync(factory, Unique("cashier-transfer-owner"), "Cliente");
        string sourceAccount = await AddPrimaryAccountAsync(factory, owner.Id, 100m, cashier.Id);
        string destinationAccount = UniqueAccountNumber();
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, cashierName);

        string page = await GetBodyAsync(client, "/cashier/third-party-transfer");
        using HttpResponseMessage response = await client.PostAsync(
            "/cashier/third-party-transfer",
            Form(
                ("__RequestVerificationToken", ExtractInput(page, "__RequestVerificationToken")),
                ("Form.SourceAccountNumber", sourceAccount),
                ("Form.DestinationAccountNumber", destinationAccount),
                ("Form.Amount", "10.00")
            )
        );
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "El número de cuenta destino ingresado no corresponde a una cuenta válida.",
            body,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("/cashier/card-payment", "No hay tarjetas activas disponibles.")]
    [InlineData("/cashier/loan-payment", "No hay préstamos activos disponibles.")]
    public async Task Cashier_product_payment_pages_render_empty_states_without_products(
        string path,
        string emptyMessage
    ) {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("cashier-products");
        await CreateUserAsync(factory, userName, "Cajero");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.GetAsync(path);
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(emptyMessage, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_operations_validate_query_filters_before_the_application_query() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("cashier-filters");
        await CreateUserAsync(factory, userName, "Cajero");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.GetAsync(
            "/cashier/operations?operationType=NotAnOperation"
        );
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("El tipo de operación seleccionado no es válido.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_cashier_cannot_enter_cashier_routes_by_direct_url() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("cashier-route");
        await CreateUserAsync(factory, userName, "Administrador");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.GetAsync("/cashier/deposit");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Auth/AccessDenied", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_forbidden_confirmation_uses_a_safe_access_denied_message() {
        Mock<ICashierOperationsService> cashier = new();
        cashier.Setup(service => service.ConfirmDepositAsync(
                It.IsAny<DepositViewModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Failure<TransactionResultViewModel>(
                DomainError.Forbidden("Auth.Forbidden", "IdentityUser internal details")
            ));

        using WebApplicationFactory<WebApp::Program> factory = CreateFactory(services => {
            services.RemoveAll<ICashierOperationsService>();
            services.AddSingleton(cashier.Object);
        });
        string userName = Unique("cashier-redaction");
        await CreateUserAsync(factory, userName, "Cajero");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        string page = await GetBodyAsync(client, "/cashier/deposit");
        using HttpResponseMessage response = await client.PostAsync(
            "/cashier/deposit/confirm",
            Form(
                ("__RequestVerificationToken", ExtractInput(page, "__RequestVerificationToken")),
                ("ConfirmationToken", "server-issued-confirmation-token"),
                ("AccountNumber", "000000001"),
                ("RequestedAmount", "10.00")
            )
        );

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Auth/AccessDenied", response.Headers.Location?.ToString());

        using HttpResponseMessage accessDenied = await client.GetAsync("/Auth/AccessDenied");
        string body = WebUtility.HtmlDecode(await accessDenied.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, accessDenied.StatusCode);
        Assert.Contains("No tiene permiso para realizar esta operación.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentityUser internal details", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_mutation_without_antiforgery_is_rejected() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        string userName = Unique("cashier-antiforgery");
        await CreateUserAsync(factory, userName, "Cajero");
        using HttpClient client = CreateClient(factory);
        await LoginAsync(client, userName);

        using HttpResponseMessage response = await client.PostAsync(
            "/cashier/deposit",
            Form(
                ("Form.AccountNumber", "000000001"),
                ("Form.Amount", "10.00")
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cashier_deposit_confirmation_is_single_use_and_does_not_double_credit() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        AppUser cashier = await CreateUserAsync(factory, Unique("cashier-confirm"), "Cajero");
        AppUser client = await CreateUserAsync(factory, Unique("cashier-owner"), "Cliente");
        string accountNumber = await AddPrimaryAccountAsync(factory, client.Id, 100m, cashier.Id);
        using HttpClient httpClient = CreateClient(factory);
        await LoginAsync(httpClient, cashier.UserName!);

        string depositPage = await GetBodyAsync(httpClient, "/cashier/deposit");
        string depositAntiforgery = ExtractInput(depositPage, "__RequestVerificationToken");
        using HttpResponseMessage prepareResponse = await httpClient.PostAsync(
            "/cashier/deposit",
            Form(
                ("__RequestVerificationToken", depositAntiforgery),
                ("Form.AccountNumber", accountNumber),
                ("Form.Amount", "50.00")
            )
        );
        string confirmationPage = WebUtility.HtmlDecode(
            await prepareResponse.Content.ReadAsStringAsync()
        );

        Assert.Equal(HttpStatusCode.OK, prepareResponse.StatusCode);
        string confirmationToken = ExtractInput(confirmationPage, "ConfirmationToken");
        string confirmationAntiforgery = ExtractInput(
            confirmationPage,
            "__RequestVerificationToken"
        );
        FormUrlEncodedContent confirmation = Form(
            ("__RequestVerificationToken", confirmationAntiforgery),
            ("ConfirmationToken", confirmationToken),
            ("AccountNumber", accountNumber),
            ("RequestedAmount", "50.00")
        );

        using HttpResponseMessage firstConfirmation = await httpClient.PostAsync(
            "/cashier/deposit/confirm",
            confirmation
        );
        Assert.Equal(HttpStatusCode.Redirect, firstConfirmation.StatusCode);
        Assert.Equal("/Home/Cashier", firstConfirmation.Headers.Location?.ToString());

        using HttpResponseMessage replay = await httpClient.PostAsync(
            "/cashier/deposit/confirm",
            Form(
                ("__RequestVerificationToken", confirmationAntiforgery),
                ("ConfirmationToken", confirmationToken),
                ("AccountNumber", accountNumber),
                ("RequestedAmount", "50.00")
            )
        );
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        Assert.Equal("/cashier/deposit", replay.Headers.Location?.ToString());

        await WithContextAsync(async context => {
            SavingsAccount account = (await new SavingsAccountRepository(context)
                .GetByNumberAsync(AccountNumber.Create(accountNumber).Value))!;
            account.Balance.Amount.Should().Be(150m);
            (await context.FinancialOperations.CountAsync(operation =>
                operation.InitiatedByUserId == cashier.Id
                && operation.Kind == FinancialOperationKind.Deposit
            )).Should().Be(1);
        });
    }

    [Fact]
    public async Task Cashier_confirmation_rejects_tampered_amount_without_changing_balance() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        AppUser cashier = await CreateUserAsync(factory, Unique("cashier-tampered"), "Cajero");
        AppUser client = await CreateUserAsync(factory, Unique("cashier-tampered-owner"), "Cliente");
        string accountNumber = await AddPrimaryAccountAsync(factory, client.Id, 100m, cashier.Id);
        using HttpClient httpClient = CreateClient(factory);
        await LoginAsync(httpClient, cashier.UserName!);

        string depositPage = await GetBodyAsync(httpClient, "/cashier/deposit");
        string depositAntiforgery = ExtractInput(depositPage, "__RequestVerificationToken");
        using HttpResponseMessage prepareResponse = await httpClient.PostAsync(
            "/cashier/deposit",
            Form(
                ("__RequestVerificationToken", depositAntiforgery),
                ("Form.AccountNumber", accountNumber),
                ("Form.Amount", "50.00")
            )
        );
        string confirmationPage = WebUtility.HtmlDecode(
            await prepareResponse.Content.ReadAsStringAsync()
        );
        string confirmationToken = ExtractInput(confirmationPage, "ConfirmationToken");
        string confirmationAntiforgery = ExtractInput(
            confirmationPage,
            "__RequestVerificationToken"
        );

        using HttpResponseMessage tampered = await httpClient.PostAsync(
            "/cashier/deposit/confirm",
            Form(
                ("__RequestVerificationToken", confirmationAntiforgery),
                ("ConfirmationToken", confirmationToken),
                ("AccountNumber", accountNumber),
                ("RequestedAmount", "60.00")
            )
        );

        Assert.Equal(HttpStatusCode.Redirect, tampered.StatusCode);
        Assert.Equal("/cashier/deposit", tampered.Headers.Location?.ToString());

        await WithContextAsync(async context => {
            SavingsAccount account = (await new SavingsAccountRepository(context)
                .GetByNumberAsync(AccountNumber.Create(accountNumber).Value))!;
            account.Balance.Amount.Should().Be(100m);
            (await context.FinancialOperations.CountAsync(operation =>
                operation.InitiatedByUserId == cashier.Id
                && operation.Kind == FinancialOperationKind.Deposit
            )).Should().Be(0);
        });
    }

    [Fact]
    public async Task Cashier_rejected_withdrawal_redirects_and_history_hides_internal_reason_code() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        AppUser cashier = await CreateUserAsync(factory, Unique("cashier-rejected"), "Cajero");
        AppUser client = await CreateUserAsync(factory, Unique("cashier-rejected-owner"), "Cliente");
        string accountNumber = await AddPrimaryAccountAsync(factory, client.Id, 100m, cashier.Id);
        using HttpClient httpClient = CreateClient(factory);
        await LoginAsync(httpClient, cashier.UserName!);

        string withdrawalPage = await GetBodyAsync(httpClient, "/cashier/withdrawal");
        string antiforgery = ExtractInput(withdrawalPage, "__RequestVerificationToken");
        using HttpResponseMessage prepareResponse = await httpClient.PostAsync(
            "/cashier/withdrawal",
            Form(
                ("__RequestVerificationToken", antiforgery),
                ("Form.AccountNumber", accountNumber),
                ("Form.Amount", "75.00")
            )
        );
        string staleConfirmation = WebUtility.HtmlDecode(
            await prepareResponse.Content.ReadAsStringAsync()
        );
        string staleToken = ExtractInput(staleConfirmation, "ConfirmationToken");
        string staleAntiforgery = ExtractInput(staleConfirmation, "__RequestVerificationToken");

        await CompleteWithdrawalAsync(httpClient, accountNumber, "100.00");

        using HttpResponseMessage rejected = await httpClient.PostAsync(
            "/cashier/withdrawal/confirm",
            Form(
                ("__RequestVerificationToken", staleAntiforgery),
                ("ConfirmationToken", staleToken),
                ("AccountNumber", accountNumber),
                ("RequestedAmount", "75.00")
            )
        );
        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        Assert.Equal("/cashier/withdrawal", rejected.Headers.Location?.ToString());

        using HttpResponseMessage history = await httpClient.GetAsync(
            "/cashier/operations?operationType=Withdrawal"
        );
        string body = WebUtility.HtmlDecode(await history.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Contains("Rechazado", body, StringComparison.Ordinal);
        Assert.Contains(
            "El monto ingresado excede el saldo disponible de la cuenta.",
            body,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Account.InsufficientFunds", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_history_uses_spanish_operation_labels_without_technical_enum_names() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        AppUser cashier = await CreateUserAsync(factory, Unique("cashier-history"), "Cajero");
        AppUser client = await CreateUserAsync(factory, Unique("cashier-history-owner"), "Cliente");
        string accountNumber = await AddPrimaryAccountAsync(factory, client.Id, 100m, cashier.Id);
        using HttpClient httpClient = CreateClient(factory);
        await LoginAsync(httpClient, cashier.UserName!);

        await CompleteDepositAsync(httpClient, accountNumber, "25.00");

        using HttpResponseMessage response = await httpClient.GetAsync(
            "/cashier/operations?operationType=Deposit"
        );
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Depósito", body, StringComparison.Ordinal);
        Assert.DoesNotContain(">Deposit<", body, StringComparison.Ordinal);
        Assert.DoesNotContain("RejectionCode", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_history_pagination_preserves_the_operation_filter() {
        using WebApplicationFactory<WebApp::Program> factory = CreateFactory();
        AppUser cashier = await CreateUserAsync(factory, Unique("cashier-pagination"), "Cajero");
        AppUser client = await CreateUserAsync(factory, Unique("cashier-pagination-owner"), "Cliente");
        string accountNumber = await AddPrimaryAccountAsync(factory, client.Id, 100m, cashier.Id);
        using HttpClient httpClient = CreateClient(factory);
        await LoginAsync(httpClient, cashier.UserName!);

        for (int index = 0; index < 21; index++) {
            await CompleteDepositAsync(httpClient, accountNumber, "1.00");
        }

        using HttpResponseMessage response = await httpClient.GetAsync(
            "/cashier/operations?operationType=Deposit&page=2"
        );
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Página 2 de 2", WebUtility.HtmlDecode(body), StringComparison.Ordinal);
        Assert.Contains("operationType=Deposit", body, StringComparison.Ordinal);
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
            FirstName = "Web",
            LastName = "Cajero",
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

    private static async Task<string> AddPrimaryAccountAsync(
        WebApplicationFactory<WebApp::Program> factory,
        string ownerUserId,
        decimal balance,
        string createdByUserId
    ) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        INumberGenerator numberGenerator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
        ISavingsAccountRepository accounts = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        string rawNumber = await numberGenerator.NextAccountNumberAsync();
        AccountNumber number = AccountNumber.Create(rawNumber).Value;
        SavingsAccount account = SavingsAccount.OpenPrimary(
            ownerUserId,
            number,
            Money.Create(balance).Value,
            createdByUserId,
            DateTimeOffset.UtcNow
        ).Value;

        await unitOfWork.ExecuteInTransactionAsync(async ct => {
            await accounts.AddAsync(account, ct);
            return Result.Success();
        });
        return number.Value;
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

    private WebApplicationFactory<WebApp::Program> CreateFactory(
        Action<IServiceCollection>? configure = null
    ) {
        string keyRingPath = Path.Combine(
            Path.GetTempPath(),
            "artemis-webapp-cashier-tests",
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
            builder.ConfigureServices(services => configure?.Invoke(services));
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

    private static async Task CompleteDepositAsync(
        HttpClient client,
        string accountNumber,
        string amount
    ) {
        string depositPage = await GetBodyAsync(client, "/cashier/deposit");
        using HttpResponseMessage prepareResponse = await client.PostAsync(
            "/cashier/deposit",
            Form(
                ("__RequestVerificationToken", ExtractInput(depositPage, "__RequestVerificationToken")),
                ("Form.AccountNumber", accountNumber),
                ("Form.Amount", amount)
            )
        );
        Assert.Equal(HttpStatusCode.OK, prepareResponse.StatusCode);
        string confirmationPage = WebUtility.HtmlDecode(
            await prepareResponse.Content.ReadAsStringAsync()
        );

        using HttpResponseMessage confirmationResponse = await client.PostAsync(
            "/cashier/deposit/confirm",
            Form(
                ("__RequestVerificationToken", ExtractInput(confirmationPage, "__RequestVerificationToken")),
                ("ConfirmationToken", ExtractInput(confirmationPage, "ConfirmationToken")),
                ("AccountNumber", accountNumber),
                ("RequestedAmount", amount)
            )
        );
        Assert.Equal(HttpStatusCode.Redirect, confirmationResponse.StatusCode);
        Assert.Equal("/Home/Cashier", confirmationResponse.Headers.Location?.ToString());
    }

    private static async Task CompleteWithdrawalAsync(
        HttpClient client,
        string accountNumber,
        string amount
    ) {
        string withdrawalPage = await GetBodyAsync(client, "/cashier/withdrawal");
        using HttpResponseMessage prepareResponse = await client.PostAsync(
            "/cashier/withdrawal",
            Form(
                ("__RequestVerificationToken", ExtractInput(withdrawalPage, "__RequestVerificationToken")),
                ("Form.AccountNumber", accountNumber),
                ("Form.Amount", amount)
            )
        );
        Assert.Equal(HttpStatusCode.OK, prepareResponse.StatusCode);
        string confirmationPage = WebUtility.HtmlDecode(
            await prepareResponse.Content.ReadAsStringAsync()
        );

        using HttpResponseMessage confirmationResponse = await client.PostAsync(
            "/cashier/withdrawal/confirm",
            Form(
                ("__RequestVerificationToken", ExtractInput(confirmationPage, "__RequestVerificationToken")),
                ("ConfirmationToken", ExtractInput(confirmationPage, "ConfirmationToken")),
                ("AccountNumber", accountNumber),
                ("RequestedAmount", amount)
            )
        );
        Assert.Equal(HttpStatusCode.Redirect, confirmationResponse.StatusCode);
        Assert.Equal("/Home/Cashier", confirmationResponse.Headers.Location?.ToString());
    }

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
        $"web-{prefix}-{Guid.NewGuid():N}"[..48];

    private static string UniqueAccountNumber() =>
        new string(
            (Guid.NewGuid().ToString("N") + "123456789")
                .Where(char.IsDigit)
                .Take(9)
                .ToArray()
        );

    private static string UniqueDocument() =>
        Guid.NewGuid().ToString("N")[..11];
}
