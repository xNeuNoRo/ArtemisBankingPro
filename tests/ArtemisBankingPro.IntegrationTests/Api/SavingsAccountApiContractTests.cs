using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.IntegrationTests.Infrastructure;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Api;

[Collection("Api")]
public sealed class SavingsAccountApiContractTests(ApiFactory factory) {
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
    );

    [Fact]
    public async Task Create_and_list_use_documented_account_contract() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser client, _) = await CreateUserAsync("Cliente");
        await CreatePrimaryAccountAsync(client.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);

        using HttpResponseMessage create = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/savings-account",
            token,
            new { clientId = client.Id, initialBalance = 5_000m },
            "savings-create-" + Guid.NewGuid().ToString("N")
        );

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument body = await ReadJsonAsync(create);
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal(
                "id", "accountNumber", "clientId", "clientFullName", "balance", "type",
                "status", "createdAt");
        body.RootElement.GetProperty("clientId").GetString().Should().Be(client.Id);
        body.RootElement.GetProperty("balance").GetDecimal().Should().Be(5_000m);
        body.RootElement.GetProperty("type").GetString().Should().Be("Secundaria");
        body.RootElement.GetProperty("status").GetString().Should().Be("Activa");
        body.RootElement.ToString().Should().NotContain("rowVersion");
        body.RootElement.ToString().Should().NotContain("password");

        using JsonDocument list = await ReadJsonAsync(
            await SendAuthenticatedAsync(HttpMethod.Get, "/api/savings-account", token, null)
        );
        JsonElement created = list.RootElement.GetProperty("data")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == body.RootElement.GetProperty("id").GetString());
        created.GetProperty("type").GetString().Should().Be("Secundaria");
        created.GetProperty("status").GetString().Should().Be("Activa");
    }

    [Fact]
    public async Task Creating_zero_balance_account_does_not_create_initial_credit_history() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser client, _) = await CreateUserAsync("Cliente");
        await CreatePrimaryAccountAsync(client.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);

        string accountNumber = await CreateSecondaryAsync(client.Id, token, 0m, "savings-zero");

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        (await context.FinancialOperations.AnyAsync(operation =>
                operation.AccountTransactions.Any(transaction =>
                    transaction.AccountNumber == AccountNumber.Create(accountNumber).Value)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task List_defaults_to_active_but_identification_without_status_includes_history() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser client, _) = await CreateUserAsync("Cliente");
        await CreatePrimaryAccountAsync(client.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);

        string cancelledNumber = await CreateSecondaryAsync(client.Id, token, 0m, "savings-cancelled");
        using HttpResponseMessage cancelled = await SendAuthenticatedAsync(
            HttpMethod.Patch,
            $"/api/savings-account/{cancelledNumber}/cancel",
            token,
            null,
            "savings-cancel-" + Guid.NewGuid().ToString("N")
        );
        cancelled.StatusCode.Should().Be(HttpStatusCode.NoContent);

        string activeNumber = await CreateSecondaryAsync(client.Id, token, 0m, "savings-active");

        using JsonDocument defaultList = await ReadJsonAsync(
            await SendAuthenticatedAsync(HttpMethod.Get, "/api/savings-account", token, null)
        );
        defaultList.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("accountNumber").GetString())
            .Should().Contain(activeNumber)
            .And.NotContain(cancelledNumber);

        using JsonDocument history = await ReadJsonAsync(
            await SendAuthenticatedAsync(
                HttpMethod.Get,
                $"/api/savings-account?identification={client.IdentityDocument}",
                token,
                null
            )
        );
        history.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("accountNumber").GetString())
            .Should().ContainInOrder(activeNumber, cancelledNumber);

        using JsonDocument cancelledOnly = await ReadJsonAsync(
            await SendAuthenticatedAsync(
                HttpMethod.Get,
                "/api/savings-account?status=cancelada&type=secundaria",
                token,
                null
            )
        );
        cancelledOnly.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("accountNumber").GetString())
            .Should().Contain(cancelledNumber)
            .And.NotContain(activeNumber);

        using JsonDocument unknownCustomer = await ReadJsonAsync(
            await SendAuthenticatedAsync(
                HttpMethod.Get,
                "/api/savings-account?identification=00000000000",
                token,
                null
            )
        );
        unknownCustomer.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
        unknownCustomer.RootElement.GetProperty("totalRecords").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Api_enforces_auth_validation_and_idempotency_key() {
        using HttpResponseMessage anonymous = await _client.GetAsync("/api/savings-account");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser client, _) = await CreateUserAsync("Cliente");
        string adminToken = await LoginTokenAsync(admin.UserName!, adminPassword);
        string clientToken = await TokenForAsync(client, "Cliente");

        using HttpResponseMessage forbidden = await SendAuthenticatedAsync(
            HttpMethod.Get,
            "/api/savings-account",
            clientToken,
            null
        );
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using HttpResponseMessage invalidPage = await SendAuthenticatedAsync(
            HttpMethod.Get,
            "/api/savings-account?page=0&pageSize=21&status=invalid&type=invalid",
            adminToken,
            null
        );
        invalidPage.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage missingBodyFields = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/savings-account",
            adminToken,
            new { },
            "savings-missing-fields-" + Guid.NewGuid().ToString("N")
        );
        missingBodyFields.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage missingKey = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/savings-account",
            adminToken,
            new { clientId = client.Id, initialBalance = 0m }
        );
        missingKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage noPrincipal = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/savings-account",
            adminToken,
            new { clientId = client.Id, initialBalance = 0m },
            "savings-no-principal-" + Guid.NewGuid().ToString("N")
        );
        noPrincipal.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reusing_creation_idempotency_key_does_not_create_twice() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser client, _) = await CreateUserAsync("Cliente");
        await CreatePrimaryAccountAsync(client.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);
        string key = "savings-idempotency-" + Guid.NewGuid().ToString("N");
        var body = new { clientId = client.Id, initialBalance = 100m };

        using HttpResponseMessage first = await SendAuthenticatedAsync(
            HttpMethod.Post, "/api/savings-account", token, body, key);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using HttpResponseMessage duplicate = await SendAuthenticatedAsync(
            HttpMethod.Post, "/api/savings-account", token, body, key);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using HttpResponseMessage reuse = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/savings-account",
            token,
            new { clientId = client.Id, initialBalance = 101m },
            key
        );
        reuse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        (await context.SavingsAccounts.CountAsync(account => account.OwnerUserId == client.Id))
            .Should().Be(2);
    }

    [Fact]
    public async Task Transactions_return_paged_history_newest_first() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser client, _) = await CreateUserAsync("Cliente");
        await CreatePrimaryAccountAsync(client.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);
        string accountNumber = await CreateSecondaryAsync(client.Id, token, 100m, "savings-history");

        await AddCreditOperationAsync(accountNumber, admin.Id, 250m);

        using HttpResponseMessage response = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"/api/savings-account/{accountNumber}/transactions?page=1&pageSize=20",
            token,
            null
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = await ReadJsonAsync(response);
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("accountNumber", "clientFullName", "balance", "type", "status", "transactions");
        JsonElement transactions = body.RootElement.GetProperty("transactions");
        transactions.GetProperty("data").GetArrayLength().Should().Be(2);
        transactions.GetProperty("data")[0].GetProperty("amount").GetDecimal().Should().Be(250m);
        transactions.GetProperty("data")[0].GetProperty("transactionType").GetString().Should().Be("CRÉDITO");
        transactions.GetProperty("data")[0].GetProperty("status").GetString().Should().Be("APROBADA");
        transactions.GetProperty("data")[1].GetProperty("amount").GetDecimal().Should().Be(100m);
    }

    [Fact]
    public async Task Cancel_with_balance_transfers_atomically_and_preserves_history() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser client, _) = await CreateUserAsync("Cliente");
        string principalNumber = await CreatePrimaryAccountAsync(client.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);
        string secondaryNumber = await CreateSecondaryAsync(client.Id, token, 300m, "savings-transfer");

        using HttpResponseMessage response = await SendAuthenticatedAsync(
            HttpMethod.Patch,
            $"/api/savings-account/{secondaryNumber}/cancel",
            token,
            null,
            "savings-cancel-transfer-" + Guid.NewGuid().ToString("N")
        );
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        SavingsAccount secondary = await context.SavingsAccounts
            .SingleAsync(account => account.Number == AccountNumber.Create(secondaryNumber).Value);
        SavingsAccount principal = await context.SavingsAccounts
            .SingleAsync(account => account.Number == AccountNumber.Create(principalNumber).Value);
        secondary.Status.Should().Be(AccountStatus.Cancelled);
        secondary.Balance.Amount.Should().Be(0m);
        principal.Balance.Amount.Should().Be(300m);
        (await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.SecondaryAccountClosureTransfer
                && operation.Status == FinancialOperationStatus.Approved
                && operation.AccountTransactions.Any(transaction =>
                    transaction.AccountNumber == AccountNumber.Create(secondaryNumber).Value)))
            .Should().Be(1);
        (await context.AccountTransactions.CountAsync(transaction =>
                transaction.AccountNumber == AccountNumber.Create(secondaryNumber).Value))
            .Should().Be(2);
    }

    [Fact]
    public async Task Primary_account_cannot_be_cancelled_and_unknown_account_is_not_found() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser client, _) = await CreateUserAsync("Cliente");
        string principalNumber = await CreatePrimaryAccountAsync(client.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);

        using HttpResponseMessage primary = await SendAuthenticatedAsync(
            HttpMethod.Patch,
            $"/api/savings-account/{principalNumber}/cancel",
            token,
            null,
            "savings-cancel-primary-" + Guid.NewGuid().ToString("N")
        );
        primary.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        primary.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using JsonDocument primaryProblem = await ReadJsonAsync(primary);
        primaryProblem.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("Account.PrincipalCannotBeCancelled");

        using HttpResponseMessage unknown = await SendAuthenticatedAsync(
            HttpMethod.Get,
            "/api/savings-account/999999999/transactions",
            token,
            null
        );
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Concurrent_cancellation_transfers_balance_once() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser client, _) = await CreateUserAsync("Cliente");
        string principalNumber = await CreatePrimaryAccountAsync(client.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);
        string secondaryNumber = await CreateSecondaryAsync(client.Id, token, 300m, "savings-concurrent");

        Task<HttpResponseMessage>[] requests = [
            SendAuthenticatedAsync(
                HttpMethod.Patch,
                $"/api/savings-account/{secondaryNumber}/cancel",
                token,
                null,
                "savings-concurrent-a-" + Guid.NewGuid().ToString("N")
            ),
            SendAuthenticatedAsync(
                HttpMethod.Patch,
                $"/api/savings-account/{secondaryNumber}/cancel",
                token,
                null,
                "savings-concurrent-b-" + Guid.NewGuid().ToString("N")
            ),
        ];
        HttpResponseMessage[] responses = await Task.WhenAll(requests);
        responses.Count(response => response.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        responses.Count(response =>
                response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
            .Should().Be(1);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        SavingsAccount principal = await context.SavingsAccounts
            .SingleAsync(account => account.Number == AccountNumber.Create(principalNumber).Value);
        principal.Balance.Amount.Should().Be(300m);
        (await context.FinancialOperations.CountAsync(operation =>
                operation.Kind == FinancialOperationKind.SecondaryAccountClosureTransfer
                && operation.Status == FinancialOperationStatus.Approved
                && operation.AccountTransactions.Any(transaction =>
                    transaction.AccountNumber == AccountNumber.Create(secondaryNumber).Value)))
            .Should().Be(1);
    }

    private async Task<string> CreateSecondaryAsync(
        string clientId,
        string token,
        decimal initialBalance,
        string keyPrefix
    ) {
        using HttpResponseMessage response = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/savings-account",
            token,
            new { clientId, initialBalance },
            keyPrefix + "-" + Guid.NewGuid().ToString("N")
        );
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("accountNumber").GetString()!;
    }

    private async Task<(AppUser User, string Password)> CreateUserAsync(string role) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        string userName = $"savings-{role.ToLowerInvariant()}-{Guid.NewGuid():N}"[..40];
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.test",
            FirstName = "Savings",
            LastName = "Contract",
            IdentityDocument = DigitsFromGuid(),
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        UserManager<AppUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        const string password = "P@ssw0rd123!";
        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return (user, password);
    }

    private async Task<string> CreatePrimaryAccountAsync(string ownerUserId, string creatorUserId) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        SavingsAccount account = SavingsAccount.OpenPrimary(
            ownerUserId,
            AccountNumber.Create(DigitsFromGuid()[..9]).Value,
            Money.Zero,
            creatorUserId,
            DateTimeOffset.UtcNow
        ).Value;
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        context.SavingsAccounts.Add(account);
        await context.SaveChangesAsync();
        return account.Number.Value;
    }

    private async Task AddCreditOperationAsync(
        string accountNumber,
        string initiatedByUserId,
        decimal amount
    ) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        AccountNumber number = AccountNumber.Create(accountNumber).Value;
        SavingsAccount account = await context.SavingsAccounts.SingleAsync(item => item.Number == number);
        account.Credit(Money.Create(amount).Value).IsSuccess.Should().BeTrue();
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow.AddMinutes(1);
        FinancialOperation operation = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.AdministrativeFunding,
            Money.Create(amount).Value,
            Money.Create(amount).Value,
            Money.Zero,
            initiatedByUserId,
            occurredAt,
            [new AccountTransactionDetails(
                number,
                TransactionDirection.Credit,
                Money.Create(amount).Value,
                "ADMINISTRATIVE_FUNDING",
                number.Value)]
        ).Value;
        context.FinancialOperations.Add(operation);
        await context.SaveChangesAsync();
    }

    private async Task<string> LoginTokenAsync(string userName, string password) {
        using HttpResponseMessage login = await _client.PostAsJsonAsync(
            "/account/login", new { userName, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = await ReadJsonAsync(login);
        return body.RootElement.GetProperty("jwt").GetString()!;
    }

    private async Task<string> TokenForAsync(AppUser user, string role) {
        if (!RoleSets.Api.Contains(role, StringComparer.Ordinal)) {
            return TestJwtFactory.Create(user.Id, user.UserName!, role);
        }

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IJwtTokenService tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return tokenService.GenerateToken(new JwtTokenRequest(
            user.Id,
            user.UserName!,
            role,
            null,
            DateTimeOffset.UtcNow
        )).Token;
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpMethod method,
        string uri,
        string token,
        object? body,
        string? idempotencyKey = null
    ) {
        var request = new HttpRequestMessage(method, uri) {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey is not null) {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return await _client.SendAsync(request);
    }

    private static string DigitsFromGuid() =>
        new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).Concat("00000000000").Take(11).ToArray());

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
