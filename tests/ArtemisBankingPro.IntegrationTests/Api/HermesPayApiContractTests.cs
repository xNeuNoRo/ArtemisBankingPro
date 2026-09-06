using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using ArtemisBankingPro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Api;

[Collection("Api")]
public sealed class HermesPayApiContractTests(ApiFactory factory) {
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
    );

    [Fact]
    public async Task Hermes_routes_require_jwt_and_an_allowed_role() {
        using HttpResponseMessage anonymousQuery = await _client.GetAsync(
            "/pay/get-transactions/1"
        );
        using HttpResponseMessage anonymousPayment = await _client.PostAsJsonAsync(
            "/pay/process-payment/1",
            ValidPaymentBody()
        );

        anonymousQuery.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymousPayment.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (AppUser client, _) = await CreateUserAsync(nameof(Roles.Cliente));
        string clientToken = await TokenForAsync(client, nameof(Roles.Cliente));

        using HttpResponseMessage forbidden = await SendAuthenticatedAsync(
            HttpMethod.Get,
            "/pay/get-transactions/1",
            clientToken,
            null
        );

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Hermes_validates_pagination_and_requires_idempotency_key() {
        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));
        string token = await LoginTokenAsync(admin.UserName!, password);

        using HttpResponseMessage invalidQuery = await SendAuthenticatedAsync(
            HttpMethod.Get,
            "/pay/get-transactions/1?page=0&pageSize=21",
            token,
            null
        );
        invalidQuery.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage missingKey = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/pay/process-payment/1",
            token,
            ValidPaymentBody()
        );
        missingKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument missingKeyBody = JsonDocument.Parse(
            await missingKey.Content.ReadAsStringAsync()
        );
        missingKeyBody.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("Idempotency.MissingKey");
    }

    [Fact]
    public async Task Administrator_can_query_transactions_with_documented_shape() {
        (AppUser admin, string password) = await CreateUserAsync(nameof(Roles.Administrador));
        Merchant merchant = await CreateMerchantAsync(admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, password);

        using HttpResponseMessage response = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"/pay/get-transactions/{merchant.Id}",
            token,
            null
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal(
                "page", "pageSize", "totalRecords", "totalPages", "commerceId", "commerceName", "data");
        body.RootElement.GetProperty("commerceId").GetInt32().Should().Be(merchant.Id);
        body.RootElement.GetProperty("commerceName").GetString().Should().Be(merchant.Name);
        body.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Commerce_role_ignores_route_commerce_id_and_reads_own_transactions() {
        (AppUser commerceUser, _) = await CreateUserAsync(nameof(Roles.Comercio));
        (AppUser admin, _) = await CreateUserAsync(nameof(Roles.Administrador));
        Merchant ownMerchant = await CreateMerchantAsync(admin.Id, commerceUser.Id);
        Merchant otherMerchant = await CreateMerchantAsync(admin.Id);
        string token = await TokenForAsync(commerceUser, nameof(Roles.Comercio), ownMerchant.Id);

        using HttpResponseMessage response = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"/pay/get-transactions/{otherMerchant.Id}",
            token,
            null
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("commerceId").GetInt32().Should().Be(ownMerchant.Id);
    }

    [Fact]
    public async Task Approved_payment_returns_204_is_idempotent_and_is_queryable() {
        (AppUser admin, string adminPassword) = await CreateUserAsync(nameof(Roles.Administrador));
        (AppUser commerceUser, _) = await CreateUserAsync(nameof(Roles.Comercio));
        (AppUser cardOwner, _) = await CreateUserAsync(nameof(Roles.Cliente));
        PaymentScenario scenario = await CreatePaymentScenarioAsync(
            admin.Id,
            commerceUser.Id,
            cardOwner.Id,
            creditLimit: 10_000m,
            cardNumber: "1589963258467598"
        );
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);
        object body = await ValidPaymentBodyAsync(689.25m, scenario.CardNumber);
        string key = "hermes-api-payment-" + Guid.NewGuid().ToString("N");

        using HttpResponseMessage approved = await SendAuthenticatedAsync(
            HttpMethod.Post,
            $"/pay/process-payment/{scenario.MerchantId}",
            token,
            body,
            key
        );
        approved.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await approved.Content.ReadAsByteArrayAsync()).Should().BeEmpty();

        using HttpResponseMessage duplicate = await SendAuthenticatedAsync(
            HttpMethod.Post,
            $"/pay/process-payment/{scenario.MerchantId}",
            token,
            body,
            key
        );
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using HttpResponseMessage transactions = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"/pay/get-transactions/{scenario.MerchantId}",
            token,
            null
        );
        using JsonDocument transactionBody = JsonDocument.Parse(
            await transactions.Content.ReadAsStringAsync()
        );
        transactionBody.RootElement.GetProperty("totalRecords").GetInt32().Should().Be(1);
        transactionBody.RootElement.GetProperty("data")[0]
            .GetProperty("cardLastFourDigits").GetString().Should().Be("7598");

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        (await context.CreditCards.AsNoTracking()
            .Where(card => card.Id == scenario.CardId)
            .Select(card => card.CurrentDebt.Amount)
            .SingleAsync()).Should().Be(689.25m);
    }

    [Fact]
    public async Task Insufficient_credit_returns_problem_details_and_rejected_history() {
        (AppUser admin, string adminPassword) = await CreateUserAsync(nameof(Roles.Administrador));
        (AppUser commerceUser, _) = await CreateUserAsync(nameof(Roles.Comercio));
        (AppUser cardOwner, _) = await CreateUserAsync(nameof(Roles.Cliente));
        PaymentScenario scenario = await CreatePaymentScenarioAsync(
            admin.Id,
            commerceUser.Id,
            cardOwner.Id,
            creditLimit: 100m,
            cardNumber: "1589963258467599"
        );
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);

        using HttpResponseMessage response = await SendAuthenticatedAsync(
            HttpMethod.Post,
            $"/pay/process-payment/{scenario.MerchantId}",
            token,
            await ValidPaymentBodyAsync(200m, scenario.CardNumber),
            "hermes-api-rejected-" + Guid.NewGuid().ToString("N")
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("HermesPay.InsufficientCredit");

        using HttpResponseMessage transactions = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"/pay/get-transactions/{scenario.MerchantId}",
            token,
            null
        );
        using JsonDocument transactionBody = JsonDocument.Parse(
            await transactions.Content.ReadAsStringAsync()
        );
        transactionBody.RootElement.GetProperty("data")[0]
            .GetProperty("status").GetString().Should().Be("RECHAZADO");
    }

    [Fact]
    public async Task Concurrent_http_payments_charge_card_once() {
        (AppUser admin, string adminPassword) = await CreateUserAsync(nameof(Roles.Administrador));
        (AppUser commerceUser, _) = await CreateUserAsync(nameof(Roles.Comercio));
        (AppUser cardOwner, _) = await CreateUserAsync(nameof(Roles.Cliente));
        PaymentScenario scenario = await CreatePaymentScenarioAsync(
            admin.Id,
            commerceUser.Id,
            cardOwner.Id,
            creditLimit: 800m,
            cardNumber: "1589963258467600"
        );
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);
        object body = await ValidPaymentBodyAsync(800m, scenario.CardNumber);

        HttpResponseMessage[] responses = await Task.WhenAll(
            SendAuthenticatedAsync(
                HttpMethod.Post,
                $"/pay/process-payment/{scenario.MerchantId}",
                token,
                body,
                "hermes-api-race-a-" + Guid.NewGuid().ToString("N")
            ),
            SendAuthenticatedAsync(
                HttpMethod.Post,
                $"/pay/process-payment/{scenario.MerchantId}",
                token,
                body,
                "hermes-api-race-b-" + Guid.NewGuid().ToString("N")
            )
        );

        responses.Count(response => response.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        responses.Count(response => response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
            .Should().Be(1);
        foreach (HttpResponseMessage response in responses) {
            response.Dispose();
        }

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        (await context.CreditCards.AsNoTracking()
            .Where(card => card.Id == scenario.CardId)
            .Select(card => card.CurrentDebt.Amount)
            .SingleAsync()).Should().Be(800m);
        (await context.FinancialOperations.CountAsync(operation =>
                operation.MerchantId == scenario.MerchantId
                && operation.Kind == ArtemisBankingPro.Domain.Operations.Enums.FinancialOperationKind.HermesPayment
                && operation.Status == ArtemisBankingPro.Domain.Operations.Enums.FinancialOperationStatus.Approved))
            .Should().Be(1);
    }

    private async Task<(AppUser User, string Password)> CreateUserAsync(string role) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        string userName = $"hermes-api-{role.ToLowerInvariant()}-{Guid.NewGuid():N}"[..40];
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.test",
            FirstName = "Hermes",
            LastName = "API",
            IdentityDocument = DigitsFromGuid(),
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        const string password = "P@ssw0rd123!";
        UserManager<AppUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return (user, password);
    }

    private async Task<string> LoginTokenAsync(string userName, string password) {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/account/login",
            new { userName, password }
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!.Jwt;
    }

    private async Task<string> TokenForAsync(AppUser user, string role, int? commerceId = null) {
        if (!RoleSets.Api.Contains(role, StringComparer.Ordinal)) {
            return TestJwtFactory.Create(user.Id, user.UserName!, role, commerceId);
        }

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IJwtTokenService tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return tokenService.GenerateToken(
            new JwtTokenRequest(user.Id, user.UserName!, role, commerceId, DateTimeOffset.UtcNow)
        ).Token;
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

    private static object ValidPaymentBody() => new {
        cardNumber = "1589963258467598",
        monthExpirationCard = "02",
        yearExpirationCard = "2028",
        cvc = "859",
        transactionAmount = 689.25m,
    };

    private async Task<object> ValidPaymentBodyAsync(decimal amount, string cardNumber) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        DateOnly today = scope.ServiceProvider.GetRequiredService<IBusinessClock>().Today;
        return new {
            cardNumber,
            monthExpirationCard = today.Month.ToString("00", CultureInfo.InvariantCulture),
            yearExpirationCard = (today.Year + 3).ToString(CultureInfo.InvariantCulture),
            cvc = "859",
            transactionAmount = amount,
        };
    }

    private async Task<Merchant> CreateMerchantAsync(
        string createdByUserId,
        string? associatedUserId = null
    ) {
        var merchant = Merchant.Create(
            $"Hermes API Commerce {Guid.NewGuid():N}"[..28],
            "API test commerce",
            $"{Guid.NewGuid():N}@example.test",
            "809" + Random.Shared.Next(1000000, 9999999),
            DigitsFromGuid(),
            createdByUserId,
            DateTimeOffset.UtcNow
        ).Value;
        if (associatedUserId is not null) {
            merchant.AssociateUser(associatedUserId, DateTimeOffset.UtcNow)
                .IsSuccess.Should().BeTrue();
        }

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();
        return merchant;
    }

    private async Task<PaymentScenario> CreatePaymentScenarioAsync(
        string adminUserId,
        string commerceUserId,
        string cardOwnerUserId,
        decimal creditLimit,
        string cardNumber
    ) {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IBusinessClock clock = scope.ServiceProvider.GetRequiredService<IBusinessClock>();
        ICardSecurityService cardSecurity = scope.ServiceProvider.GetRequiredService<ICardSecurityService>();
        INumberGenerator numberGenerator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        DateTimeOffset now = clock.Now;

        var merchant = Merchant.Create(
            $"Hermes Payment {Guid.NewGuid():N}"[..22],
            "API payment test",
            $"{Guid.NewGuid():N}@example.test",
            "809" + Random.Shared.Next(1000000, 9999999),
            DigitsFromGuid(),
            adminUserId,
            now
        ).Value;
        merchant.AssociateUser(commerceUserId, now).IsSuccess.Should().BeTrue();

        var account = SavingsAccount.OpenPrimary(
            commerceUserId,
            AccountNumber.Create(await numberGenerator.NextAccountNumberAsync()).Value,
            Money.Zero,
            adminUserId,
            now
        ).Value;
        var card = CreditCard.Issue(
            cardOwnerUserId,
            cardNumber[^4..],
            cardSecurity.ComputePanFingerprint(cardNumber),
            CvcDigest.Create(cardSecurity.ComputeCvcDigest("859")).Value,
            Money.Create(creditLimit).Value,
            adminUserId,
            now,
            clock.Today
        ).Value;

        context.Merchants.Add(merchant);
        context.SavingsAccounts.Add(account);
        context.CreditCards.Add(card);
        await context.SaveChangesAsync();
        return new PaymentScenario(merchant.Id, card.Id, cardNumber);
    }

    private static string DigitsFromGuid() =>
        new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).Concat("00000000000").Take(11).ToArray());

    private sealed record LoginResponse(string Jwt);

    private sealed record PaymentScenario(int MerchantId, int CardId, string CardNumber);
}
