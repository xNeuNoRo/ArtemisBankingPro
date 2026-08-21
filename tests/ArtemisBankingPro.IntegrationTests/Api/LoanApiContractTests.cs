using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ArtemisBankingPro.IntegrationTests.Api;

[Collection("Api")]
public sealed class LoanApiContractTests(ApiFactory factory) {
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
    );

    [Fact]
    public async Task Loan_endpoints_use_documented_transport_names_and_statuses() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser customer, _) = await CreateUserAsync("Cliente");
        await CreatePrimaryAccountAsync(customer.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/loan") {
            Content = JsonContent.Create(new {
                clientId = customer.Id,
                capitalAmount = 1_000m,
                termInMonths = 6,
                annualInterestRate = 0m,
                confirmHighRisk = true,
            }),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createRequest.Headers.Add("Idempotency-Key", "loan-api-" + Guid.NewGuid().ToString("N"));

        using HttpResponseMessage createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument created = await ReadJsonAsync(createResponse);
        created.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal(
                "id", "loanNumber", "clientId", "clientFullName", "capitalAmount",
                "termInMonths", "annualInterestRate", "monthlyInstallment",
                "totalAmountToPay", "status", "createdAt");
        created.RootElement.GetProperty("id").GetString().Should().NotBeNullOrWhiteSpace();
        created.RootElement.GetProperty("clientId").GetString().Should().Be(customer.Id);
        created.RootElement.GetProperty("status").GetString().Should().Be("Activo");

        string loanId = created.RootElement.GetProperty("id").GetString()!;

        using HttpResponseMessage detailResponse = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"/api/loan/{loanId}",
            token,
            null
        );
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument detail = await ReadJsonAsync(detailResponse);
        detail.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal(
                "id", "loanNumber", "clientId", "clientFullName", "capitalAmount",
                "annualInterestRate", "termInMonths", "monthlyInstallment",
                "pendingAmount", "status", "clientPaymentStatus", "createdAt", "amortization");
        detail.RootElement.GetProperty("amortization")[0]
            .EnumerateObject().Select(property => property.Name)
            .Should().Equal(
                "installmentNumber", "dueDate", "installmentAmount", "interestAmount",
                "capitalAmount", "pendingInstallmentAmount", "paymentStatus", "isLate");
        detail.RootElement.GetProperty("amortization")[0]
            .GetProperty("paymentStatus").GetString().Should().Be("Pendiente");

        using HttpResponseMessage listResponse = await SendAuthenticatedAsync(
            HttpMethod.Get,
            "/api/loan",
            token,
            null
        );
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument list = await ReadJsonAsync(listResponse);
        list.RootElement.GetProperty("data")[0]
            .EnumerateObject().Select(property => property.Name)
            .Should().Equal(
                "id", "loanNumber", "clientId", "clientFullName", "capitalAmount",
                "totalInstallments", "paidInstallments", "pendingAmount",
                "annualInterestRate", "termInMonths", "status", "clientPaymentStatus",
                "createdAt");
        list.RootElement.GetProperty("data")[0]
            .GetProperty("totalInstallments").GetInt32().Should().Be(6);
        list.RootElement.GetProperty("data")[0]
            .GetProperty("pendingAmount").GetDecimal().Should().BeGreaterThan(0m);

        using HttpResponseMessage duplicateResponse = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/loan",
            token,
            new {
                clientId = customer.Id,
                capitalAmount = 1_000m,
                termInMonths = 6,
                annualInterestRate = 0m,
                confirmHighRisk = true,
            },
            "loan-duplicate-" + Guid.NewGuid().ToString("N")
        );
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage rateResponse = await SendAuthenticatedAsync(
            HttpMethod.Patch,
            $"/api/loan/{loanId}/rate",
            token,
            new { annualInterestRate = 12m },
            "loan-rate-" + Guid.NewGuid().ToString("N")
        );
        rateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Create_loan_rejects_an_active_non_client_user() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser target, _) = await CreateUserAsync("Administrador");
        await CreatePrimaryAccountAsync(target.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);

        using HttpResponseMessage response = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/loan",
            token,
            new {
                clientId = target.Id,
                capitalAmount = 1_000m,
                termInMonths = 6,
                annualInterestRate = 0m,
                confirmHighRisk = true,
            },
            "loan-non-client-" + Guid.NewGuid().ToString("N")
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await ReadJsonAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("Loan.InvalidRequest");
    }

    [Fact]
    public async Task Create_loan_rejects_missing_required_rate() {
        (AppUser admin, string adminPassword) = await CreateUserAsync("Administrador");
        (AppUser customer, _) = await CreateUserAsync("Cliente");
        await CreatePrimaryAccountAsync(customer.Id, admin.Id);
        string token = await LoginTokenAsync(admin.UserName!, adminPassword);

        using HttpResponseMessage response = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "/api/loan",
            token,
            new {
                clientId = customer.Id,
                capitalAmount = 1_000m,
                termInMonths = 6,
                confirmHighRisk = true,
            },
            "loan-missing-rate-" + Guid.NewGuid().ToString("N")
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument problem = await ReadJsonAsync(response);
        problem.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("Validation.Failed");
        problem.RootElement.GetProperty("errors")
            .GetProperty("annualInterestRate").GetArrayLength().Should().BeGreaterThan(0);
    }

    private async Task<(AppUser User, string Password)> CreateUserAsync(string role) {
        await using var scope = factory.Services.CreateAsyncScope();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }

        string userName = $"loan-{role.ToLowerInvariant()}-{Guid.NewGuid():N}"[..40];
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.test",
            FirstName = "Loan",
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

    private async Task CreatePrimaryAccountAsync(string ownerUserId, string creatorUserId) {
        await using var scope = factory.Services.CreateAsyncScope();
        var account = SavingsAccount.OpenPrimary(
            ownerUserId,
            AccountNumber.Create(DigitsFromGuid()[..9]).Value,
            Money.Zero,
            creatorUserId,
            DateTimeOffset.UtcNow
        ).Value;
        BankingDbContext context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        context.SavingsAccounts.Add(account);
        await context.SaveChangesAsync();
    }

    private async Task<string> LoginTokenAsync(string userName, string password) {
        using HttpResponseMessage login = await _client.PostAsJsonAsync(
            "/account/login", new { userName, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = await ReadJsonAsync(login);
        return body.RootElement.GetProperty("jwt").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpMethod method,
        string uri,
        string token,
        object? body,
        string? idempotencyKey = null
    ) {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) {
            request.Content = JsonContent.Create(body);
        }
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
