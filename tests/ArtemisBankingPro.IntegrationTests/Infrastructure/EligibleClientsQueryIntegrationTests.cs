using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class EligibleClientsQueryIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    [Fact]
    public async Task Repositories_ReturnCurrentClientEligibilityAndFinancialFacts() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;
        var userManager = serviceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(nameof(Roles.Cliente))) {
            (await roleManager.CreateAsync(new IdentityRole(nameof(Roles.Cliente))))
                .Succeeded
                .Should()
                .BeTrue();
        }

        string suffix = Guid.NewGuid().ToString("N");
        AppUser loanClient = await CreateClientAsync(
            userManager,
            $"loan-{suffix}",
            $"700{suffix[..8]}"
        );
        AppUser availableClient = await CreateClientAsync(
            userManager,
            $"available-{suffix}",
            $"800{suffix[..8]}"
        );

        await AddPrincipalAccountAsync(serviceProvider, loanClient.Id, 100m);
        await AddPrincipalAccountAsync(serviceProvider, availableClient.Id, 0m);
        await AddActiveLoanAsync(serviceProvider, loanClient.Id);

        var admin = serviceProvider.GetRequiredService<IAdminRepository>();
        var users = serviceProvider.GetRequiredService<IUserRepository>();
        IReadOnlyList<string> activeClientIds = await users.GetActiveClientIdsAsync();
        IReadOnlyDictionary<string, ClientAssignmentFinancialFacts> facts =
            await admin.GetClientAssignmentFactsAsync(activeClientIds);
        IReadOnlyCollection<string> loanEligibleIds = facts
            .Where(pair => !pair.Value.HasActiveLoan)
            .Select(pair => pair.Key)
            .ToArray();
        var loanPage = await users.GetActiveClientsPagedAsync(
            loanEligibleIds,
            null,
            new ArtemisBankingPro.Domain.Common.Pagination.PageRequest(1, 20)
        );

        loanPage.Items.Should().NotContain(item => item.Id == loanClient.Id);
        loanPage.Items.Should().Contain(item => item.Id == availableClient.Id);
        facts.Values.Sum(fact => fact.TotalDebt).Should().BeGreaterThan(0m);

        IReadOnlyCollection<string> secondaryEligibleIds = facts
            .Where(pair => pair.Value.HasPrincipalSavingsAccount)
            .Select(pair => pair.Key)
            .ToArray();
        var secondaryPage = await users.GetActiveClientsPagedAsync(
            secondaryEligibleIds,
            availableClient.IdentityDocument,
            new ArtemisBankingPro.Domain.Common.Pagination.PageRequest(1, 20)
        );
        secondaryPage.Items.Should().ContainSingle(item => item.Id == availableClient.Id);
        facts[availableClient.Id].TotalDebt.Should().Be(0m);
    }

    private static async Task<AppUser> CreateClientAsync(
        UserManager<AppUser> userManager,
        string userName,
        string identityDocument
    ) {
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Cliente",
            LastName = "Elegibilidad",
            IdentityDocument = identityDocument,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, nameof(Roles.Cliente))).Succeeded.Should().BeTrue();
        return user;
    }

    private static async Task AddPrincipalAccountAsync(
        IServiceProvider serviceProvider,
        string ownerUserId,
        decimal balance
    ) {
        var generator = serviceProvider.GetRequiredService<INumberGenerator>();
        var accounts = serviceProvider.GetRequiredService<ISavingsAccountRepository>();
        var unitOfWork = serviceProvider.GetRequiredService<IUnitOfWork>();
        var account = SavingsAccount.OpenPrimary(
            ownerUserId,
            AccountNumber.Create(await generator.NextAccountNumberAsync()).Value,
            Money.Create(balance).Value,
            "eligibility-admin",
            DateTimeOffset.UtcNow
        ).Value;

        (await unitOfWork.ExecuteInTransactionAsync(async ct => {
            await accounts.AddAsync(account, ct);
            return Result.Success();
        })).IsSuccess.Should().BeTrue();
    }

    private static async Task AddActiveLoanAsync(IServiceProvider serviceProvider, string ownerUserId) {
        var generator = serviceProvider.GetRequiredService<INumberGenerator>();
        var loans = serviceProvider.GetRequiredService<ILoanRepository>();
        var unitOfWork = serviceProvider.GetRequiredService<IUnitOfWork>();
        var loan = Loan.Issue(
            ownerUserId,
            LoanNumber.Create(await generator.NextLoanNumberAsync()).Value,
            Money.Create(1000m).Value,
            12,
            InterestRate.Create(12m).Value,
            "eligibility-admin",
            DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow)
        ).Value;

        (await unitOfWork.ExecuteInTransactionAsync(async ct => {
            await loans.AddAsync(loan, ct);
            return Result.Success();
        })).IsSuccess.Should().BeTrue();
    }
}
