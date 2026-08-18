using ArtemisBankingPro.Application;
using ArtemisBankingPro.Application.Features.Admin.DTOs;
using ArtemisBankingPro.Application.Features.Admin.Queries;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

/// <summary>
/// Verifica los agregados del dashboard administrativo (spec §16-§18) contra
/// SQL Server real: conteos de operaciones/pagos por día de negocio, conteos
/// de productos activos y deuda promedio solo de clientes activos. Cierra con
/// un flujo end-to-end del handler resuelto por DI.
/// </summary>
[Collection("SqlServer")]
public sealed class AdminDashboardIntegrationTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    // Día de negocio 2026-08-06 en America/Santo_Domingo (UTC-4):
    // [2026-08-06T04:00:00Z, 2026-08-07T04:00:00Z)
    private static readonly DateOnly BusinessDate = new(2026, 8, 6);
    private static readonly DateTimeOffset DayStartUtc = new(2026, 8, 6, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayEndUtc = new(2026, 8, 7, 4, 0, 0, TimeSpan.Zero);

    private static AccountNumber Account(string number) => AccountNumber.Create(number).Value;

    private static FinancialOperation NewApproved(
        FinancialOperationKind kind,
        decimal amount,
        DateTimeOffset occurredAt,
        int? creditCardId = null,
        string? loanNumber = null,
        string initiatedBy = "admin-1"
    ) =>
        FinancialOperation
            .Approve(
                Guid.NewGuid(),
                kind,
                Money.Create(amount).Value,
                Money.Create(amount).Value,
                Money.Zero,
                initiatedBy,
                occurredAt,
                [
                    new AccountTransactionDetails(
                        Account("400000001"),
                        DirectionFor(kind),
                        Money.Create(amount).Value,
                        "400000001",
                        "400000001"
                    ),
                ],
                creditCardId: creditCardId,
                loanNumber: loanNumber is null ? null : LoanNumber.Create(loanNumber).Value)
            .Value;

    private static TransactionDirection DirectionFor(FinancialOperationKind kind) =>
        kind
            is FinancialOperationKind.InitialFunding
                or FinancialOperationKind.AdministrativeFunding
                or FinancialOperationKind.LoanDisbursement
                or FinancialOperationKind.Deposit
                or FinancialOperationKind.CashAdvance
                or FinancialOperationKind.HermesPayment
            ? TransactionDirection.Credit
            : TransactionDirection.Debit;

    private static FinancialOperation NewRejected(
        FinancialOperationKind kind,
        decimal amount,
        DateTimeOffset occurredAt
    ) =>
        FinancialOperation
            .Reject(
                Guid.NewGuid(),
                kind,
                Money.Create(amount).Value,
                Money.Zero,
                "admin-1",
                occurredAt,
                "InsufficientFunds",
                [
                    new AccountTransactionDetails(
                        Account("400000002"),
                        TransactionDirection.Debit,
                        Money.Create(amount).Value,
                        "400000002",
                        "400000002"
                    ),
                ])
            .Value;

    private static FinancialOperation NewTransfer(decimal amount, DateTimeOffset occurredAt) =>
        FinancialOperation
            .Approve(
                Guid.NewGuid(),
                FinancialOperationKind.CashierTransfer,
                Money.Create(amount).Value,
                Money.Create(amount).Value,
                Money.Zero,
                "admin-1",
                occurredAt,
                [
                    new AccountTransactionDetails(
                        Account("400000005"),
                        TransactionDirection.Debit,
                        Money.Create(amount).Value,
                        "400000005",
                        "400000006"
                    ),
                    new AccountTransactionDetails(
                        Account("400000006"),
                        TransactionDirection.Credit,
                        Money.Create(amount).Value,
                        "400000005",
                        "400000006"
                    ),
                ])
            .Value;

    private static CreditCard NewCard(
        string ownerUserId,
        decimal limit = 50_000m,
        string lastFour = "1234"
    ) =>
        CreditCard
            .Issue(
                ownerUserId,
                lastFour,
                lastFour.PadRight(64, '0'),
                CvcDigest.Create(new string(lastFour[^1], 64)).Value,
                Money.Create(limit).Value,
                "admin",
                DayStartUtc,
                BusinessDate)
            .Value;

    private static Loan NewLoan(
        string ownerUserId,
        decimal principal,
        decimal? payUntilRemainder = null,
        string suffix = "1"
    ) {
        Loan loan = Loan
            .Issue(
                ownerUserId,
                LoanNumber.Create($"30000000{suffix}").Value,
                Money.Create(principal).Value,
                12,
                InterestRate.Create(12m).Value,
                "admin",
                DayStartUtc,
                BusinessDate)
            .Value;

        // El pago se calcula sobre el saldo real (capital + intereses) para
        // dejar el remanente deseado; remainder 0 completa el préstamo.
        if (payUntilRemainder is { } remainder) {
            decimal paid = loan.OutstandingAmount.Amount - remainder;
            if (paid > 0m) {
                loan.ApplyPayment(Money.Create(paid).Value, DayStartUtc.AddMonths(1));
            }
        }

        return loan;
    }

    private static SavingsAccount NewAccount(
        string ownerUserId,
        string number,
        decimal balance,
        AccountType type,
        bool cancelled = false
    ) {
        SavingsAccount account = type == AccountType.Primary
            ? SavingsAccount
                .OpenPrimary(ownerUserId, Account(number), Money.Create(balance).Value, "admin", DayStartUtc)
                .Value
            : SavingsAccount
                .OpenSecondary(ownerUserId, Account(number), Money.Create(balance).Value, "admin", DayStartUtc)
                .Value;

        if (cancelled) {
            account.Cancel(DayStartUtc.AddHours(2));
        }

        return account;
    }

    private async Task<IBusinessClock> GetClockAsync() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<IBusinessClock>();
    }

    [Fact]
    public async Task GetOperationCountsAsync_CountsAllOperations_AndApprovedPaymentsOnly() {
        IBusinessClock clock = await GetClockAsync();

        int cardId = 0;
        await WithContextAsync(async context => {
            var card = NewCard("customer-1");
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
            cardId = card.Id;

            context.FinancialOperations.AddRange(
                NewApproved(FinancialOperationKind.Deposit, 100m, DayStartUtc.AddHours(8)),
                NewApproved(FinancialOperationKind.Withdrawal, 50m, DayStartUtc.AddHours(9)),
                NewRejected(FinancialOperationKind.Withdrawal, 30m, DayStartUtc.AddHours(10)),
                NewApproved(
                    FinancialOperationKind.CreditCardPayment,
                    700m,
                    DayStartUtc.AddHours(11),
                    creditCardId: cardId),
                NewTransfer(150m, DayStartUtc.AddHours(12)),
                NewApproved(
                    FinancialOperationKind.LoanPayment,
                    900m,
                    DayStartUtc.AddDays(-1),
                    loanNumber: "300000001"),
                NewApproved(FinancialOperationKind.Deposit, 300m, DayEndUtc.AddDays(1))
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new AdminRepository(context, clock);

            var counts = await repository.GetOperationCountsAsync(BusinessDate);

            // Todas las operaciones registradas, aprobadas o rechazadas.
            counts.TotalTransactions.Should().Be(7);
            // Solo las del día de negocio: depósito, retiro, retiro rechazado,
            // pago de tarjeta y transferencia (excluye ayer y mañana).
            counts.TodayTransactions.Should().Be(5);
            // Pagos aprobados a tarjeta o a préstamo; excluye depósitos, retiros,
            // transferencias y rechazados.
            counts.TotalPayments.Should().Be(2);
            counts.TodayPayments.Should().Be(1);
        });
    }

    [Fact]
    public async Task GetOperationCountsAsync_DayBoundary_CountsInsideBusinessRange() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            context.FinancialOperations.AddRange(
                NewApproved(FinancialOperationKind.Deposit, 10m, DayStartUtc),
                NewApproved(FinancialOperationKind.Deposit, 20m, DayEndUtc.AddTicks(-1)),
                NewApproved(FinancialOperationKind.Deposit, 30m, DayEndUtc)
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new AdminRepository(context, clock);

            var counts = await repository.GetOperationCountsAsync(BusinessDate);

            counts.TotalTransactions.Should().Be(3);
            counts.TodayTransactions.Should().Be(2);
        });
    }

    [Fact]
    public async Task CountActiveSavingsAccountsAsync_IncludesPrincipalAndSecondary_ExcludesCancelled() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            context.SavingsAccounts.AddRange(
                NewAccount("customer-1", "100000001", 1_000m, AccountType.Primary),
                NewAccount("customer-1", "100000002", 500m, AccountType.Secondary),
                NewAccount("customer-2", "100000003", 0m, AccountType.Secondary, cancelled: true)
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new AdminRepository(context, clock);

            (await repository.CountActiveSavingsAccountsAsync()).Should().Be(2);
        });
    }

    [Fact]
    public async Task CountActiveLoansAsync_ExcludesCompleted() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            context.Loans.AddRange(
                NewLoan("customer-1", 12_000m),
                NewLoan("customer-2", 6_000m, payUntilRemainder: 0m, suffix: "2")
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new AdminRepository(context, clock);

            (await repository.CountActiveLoansAsync()).Should().Be(1);
        });
    }

    [Fact]
    public async Task CountActiveCreditCardsAsync_ExcludesCancelled() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            var cancelled = NewCard("customer-2", lastFour: "5678");
            cancelled.Cancel(DayStartUtc.AddHours(3));
            context.CreditCards.AddRange(NewCard("customer-1"), cancelled);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new AdminRepository(context, clock);

            (await repository.CountActiveCreditCardsAsync()).Should().Be(1);
        });
    }

    [Fact]
    public async Task GetActiveClientDebtAsync_SumsDebtOfActiveClientsOnly() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            // Cliente A (activo): préstamo con saldo pendiente 1,000 y tarjeta con deuda 500.
            Loan loanA = NewLoan("cliente-a", 10_000m, payUntilRemainder: 1_000m);
            CreditCard cardA = NewCard("cliente-a");
            cardA.AuthorizeCharge(Money.Create(500m).Value, BusinessDate);

            // Cliente B (activo): préstamo con saldo pendiente 2,000.
            Loan loanB = NewLoan("cliente-b", 5_000m, payUntilRemainder: 2_000m, suffix: "2");

            // Cliente C (inactivo): su deuda no debe contar en el promedio.
            Loan loanC = NewLoan("cliente-c", 20_000m, suffix: "3");
            CreditCard cardC = NewCard("cliente-c", lastFour: "9012");
            cardC.AuthorizeCharge(Money.Create(9_000m).Value, BusinessDate);

            // Tarjeta cancelada de A: no cuenta (el dominio exige deuda cero
            // para cancelar, por lo que se cancela sin consumos previos).
            CreditCard cancelledCardA = NewCard("cliente-a", lastFour: "3456");
            cancelledCardA.Cancel(DayStartUtc.AddHours(4));

            // Préstamo completado de A: no cuenta (estado completado).
            Loan completedLoanA = NewLoan("cliente-a", 4_000m, payUntilRemainder: 0m, suffix: "4");

            context.Loans.AddRange(loanA, loanB, loanC, completedLoanA);
            context.CreditCards.AddRange(cardA, cardC, cancelledCardA);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new AdminRepository(context, clock);

            Money debt = await repository.GetActiveClientDebtAsync(["cliente-a", "cliente-b"]);

            // 1,000 (préstamo A) + 500 (tarjeta A) + 2,000 (préstamo B).
            debt.Amount.Should().Be(3_500m);
        });
    }

    [Fact]
    public async Task GetActiveClientDebtAsync_NoActiveClients_ReturnsZero() {
        IBusinessClock clock = await GetClockAsync();

        await WithContextAsync(async context => {
            var repository = new AdminRepository(context, clock);

            (await repository.GetActiveClientDebtAsync([])).Amount.Should().Be(0m);
        });
    }

    [Fact]
    public async Task GetAdminDashboardQueryHandler_ReturnsCompleteDashboard_ThroughDi() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var clock = scope.ServiceProvider.GetRequiredService<IBusinessClock>();

        // --- Seed: 2 clientes activos, 1 inactivo, productos y operaciones. ---
        string activeA = await CreateClientAsync(Fixture.Services, "dashboard-aa", active: true);
        string activeB = await CreateClientAsync(Fixture.Services, "dashboard-ab", active: true);
        await CreateClientAsync(Fixture.Services, "dashboard-ic", active: false);

        await WithContextAsync(async context => {
            // Productos del cliente activo A.
            var loanA = Loan
                .Issue(
                    activeA,
                    LoanNumber.Create("310000001").Value,
                    Money.Create(10_000m).Value,
                    12,
                    InterestRate.Create(12m).Value,
                    "admin",
                    clock.Now,
                    clock.Today)
                .Value;
            // El pago se calcula sobre el saldo real (capital + intereses) para
            // dejar exactamente 1,000 de saldo pendiente.
            loanA.ApplyPayment(
                Money.Create(loanA.OutstandingAmount.Amount - 1_000m).Value,
                clock.Now
            );
            var cardA = NewCard(activeA, lastFour: "1111");
            cardA.AuthorizeCharge(Money.Create(500m).Value, clock.Today);
            context.Loans.Add(loanA);
            context.CreditCards.Add(cardA);
            context.SavingsAccounts.Add(NewAccount(activeA, "100000011", 1_000m, AccountType.Primary));
            context.SavingsAccounts.Add(NewAccount(activeA, "100000012", 500m, AccountType.Secondary));

            // Cuenta del cliente inactivo: se cuenta como producto activo (spec §542-548).
            context.SavingsAccounts.Add(NewAccount(activeB, "100000013", 200m, AccountType.Primary));
            await context.SaveChangesAsync();

            var card = await context.CreditCards.SingleAsync();
            context.FinancialOperations.AddRange(
                NewApproved(FinancialOperationKind.Deposit, 100m, clock.Now),
                NewRejected(FinancialOperationKind.Withdrawal, 30m, clock.Now.AddMinutes(-5)),
                NewApproved(
                    FinancialOperationKind.CreditCardPayment,
                    700m,
                    clock.Now.AddMinutes(-10),
                    creditCardId: card.Id),
                NewApproved(
                    FinancialOperationKind.LoanPayment,
                    900m,
                    clock.Now.AddDays(-1),
                    loanNumber: "310000001")
            );
            await context.SaveChangesAsync();
        });

        // --- Ejecución del handler real a través de DI. ---
        using var applicationScope = Fixture
            .BuildProvider(configure: services => services.AddApplication())
            .CreateAsyncScope();
        var handler = applicationScope.ServiceProvider.GetRequiredService<
            IRequestHandler<GetAdminDashboardQuery, Result<AdminDashboardDto>>
        >();

        var result = await handler.Handle(new GetAdminDashboardQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        AdminDashboardDto dashboard = result.Value;

        dashboard.TotalTransactionsHistorical.Should().Be(4);
        dashboard.TransactionsToday.Should().Be(3);
        dashboard.TotalPaymentsHistorical.Should().Be(2);
        dashboard.PaymentsToday.Should().Be(1);
        dashboard.ActiveClients.Should().Be(2);
        dashboard.InactiveClients.Should().Be(1);
        dashboard.ActiveSavingsAccounts.Should().Be(3);
        dashboard.ActiveLoans.Should().Be(1);
        dashboard.ActiveCreditCards.Should().Be(1);
        dashboard.TotalFinancialProducts.Should().Be(5);
        // (1,000 de préstamo + 500 de tarjeta) / 2 clientes activos.
        dashboard.AverageDebtPerClient.Should().Be(750.00m);
    }

    private static async Task<string> CreateClientAsync(
        IServiceProvider services,
        string userName,
        bool active
    ) {
        await using var scope = services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(nameof(Roles.Cliente))) {
            (await roleManager.CreateAsync(new IdentityRole(nameof(Roles.Cliente))))
                .Succeeded.Should().BeTrue();
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Cliente",
            LastName = "Dashboard",
            IdentityDocument = $"40100{userName[^2..]}",
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, nameof(Roles.Cliente))).Succeeded.Should().BeTrue();
        return user.Id;
    }
}
