using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class LoanManagementIntegrationTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private const string AdminId = "loan-admin-id";

    private sealed class FixedCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => AdminId;
        public string? UserName => "loanadmin";
        public string? Role => "Administrador";
        public int? CommerceId => null;
    }

    private static async Task EnsureRoleAsync(IServiceProvider provider, string role) {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role)) {
            (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }
    }

    private int _documentCounter;

    private async Task<AppUser> CreateClientWithPrincipalAccountAsync(
        string userName,
        decimal initialBalance
    ) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        await EnsureRoleAsync(scope.ServiceProvider, nameof(Roles.Cliente));

        string document = $"60000{(_documentCounter++):D5}";
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Cliente",
            LastName = "Prueba",
            IdentityDocument = document,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, nameof(Roles.Cliente))).Succeeded.Should().BeTrue();

        // Crear la cuenta principal con el balance inicial vía el repositorio.
        var numberGenerator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
        var accountRepository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string rawNumber = await numberGenerator.NextAccountNumberAsync();
        var accountNumber = AccountNumber.Create(rawNumber).Value;
        var balance = Domain.Common.ValueObjects.Money.Create(initialBalance).Value;
        var account = SavingsAccount.OpenPrimary(
            user.Id,
            accountNumber,
            balance,
            AdminId,
            DateTimeOffset.UtcNow
        ).Value;

        await unitOfWork.ExecuteInTransactionAsync(async ct => {
            await accountRepository.AddAsync(account, ct);
            return Domain.Common.ValueObjects.Result.Success();
        });

        return user;
    }

    private ServiceProvider BuildProvider() =>
        Fixture.BuildProvider(configure: services =>
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser())
        );

    private static CreateLoanCommandHandler CreateLoanHandler(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<ILoanRepository>(),
            provider.GetRequiredService<ICreditCardRepository>(),
            provider.GetRequiredService<ISavingsAccountRepository>(),
            provider.GetRequiredService<ArtemisBankingPro.Application.Interfaces.Persistence.Repositories.IFinancialOperationRepository>(),
            provider.GetRequiredService<INumberGenerator>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IBusinessClock>(),
            provider.GetRequiredService<ICurrentUserService>()
        );

    [Fact]
    public async Task CreateLoan_DisbursesToPrincipalAccountAndGeneratesAmortization() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("loanclient", 5000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateLoanHandler(scope.ServiceProvider);

        var result = await handler.Handle(
            new CreateLoanCommand(client.Id, 100000m, 12, 12m, ConfirmHighRisk: true),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.LoanNumber.Should().MatchRegex(@"^\d{9}$");
        result.Value.MonthlyInstallment.Should().Be(8884.88m);
        result.Value.TotalAmountToPay.Should().BeGreaterThan(100000m);

        // El desembolso se acreditó a la cuenta principal.
        var accountRepository = scope.ServiceProvider.GetRequiredService<ISavingsAccountRepository>();
        var principal = await accountRepository.GetPrincipalByOwnerAsync(client.Id);
        Assert.NotNull(principal);
        principal.Balance.Amount.Should().Be(105000m);

        // La tabla de amortización tiene 12 cuotas pendientes.
        var loanRepository = scope.ServiceProvider.GetRequiredService<ILoanRepository>();
        var loan = await loanRepository.GetWithInstallmentsByIdAsync(result.Value.LoanId);
        Assert.NotNull(loan);
        loan.Installments.Should().HaveCount(12);
        loan.Installments.All(i => i.Status == Domain.Lending.Enums.InstallmentStatus.Pending)
            .Should().BeTrue();
    }

    [Fact]
    public async Task CreateLoan_ClientWithActiveLoan_ReturnsConflict() {
        AppUser client = await CreateClientWithPrincipalAccountAsync("loandup", 5000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateLoanHandler(scope.ServiceProvider);

        var first = await handler.Handle(
            new CreateLoanCommand(client.Id, 50000m, 12, 10m, ConfirmHighRisk: true),
            CancellationToken.None
        );
        first.IsSuccess.Should().BeTrue();

        var second = await handler.Handle(
            new CreateLoanCommand(client.Id, 50000m, 12, 10m, ConfirmHighRisk: true),
            CancellationToken.None
        );

        second.IsFailure.Should().BeTrue();
        second.Error!.Code.Should().Be("Loan.ActiveLoanExists");
    }

    [Fact]
    public async Task CreateLoan_HighRiskWithoutConfirmation_ReturnsConflict() {
        // Cliente A: deuda previa (eleva el promedio del sistema).
        AppUser clientA = await CreateClientWithPrincipalAccountAsync("loanriska", 5000m);
        // Cliente B: sin deuda, recibirá el préstamo evaluado.
        AppUser clientB = await CreateClientWithPrincipalAccountAsync("loanriskb", 5000m);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = CreateLoanHandler(scope.ServiceProvider);

        // Préstamo a A para generar deuda en el sistema.
        var seedLoan = await handler.Handle(
            new CreateLoanCommand(clientA.Id, 100000m, 12, 12m, ConfirmHighRisk: true),
            CancellationToken.None
        );
        seedLoan.IsSuccess.Should().BeTrue();

        // B: sin confirmación, la deuda proyectada supera el promedio → 409.
        var risky = await handler.Handle(
            new CreateLoanCommand(clientB.Id, 100000m, 12, 12m, ConfirmHighRisk: false),
            CancellationToken.None
        );

        risky.IsFailure.Should().BeTrue();
        risky.Error!.Code.Should().Be("Loan.HighRisk");
    }
}
