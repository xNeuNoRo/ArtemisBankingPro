using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Persistence.Common;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

/// <summary>
/// Agregados de lectura del dashboard administrativo (spec §16-§18) sobre el
/// historial financiero y los productos. Todos los conteos se calculan con
/// agregados SQL (COUNT / SELECT de columnas necesarias), sin materializar
/// filas completas; los pagos solo cuentan aprobados (tarjeta y préstamo) y
/// la deuda promedio se limita a los clientes activos recibidos.
/// </summary>
public sealed class AdminRepository : IAdminRepository {
    private static readonly FinancialOperationKind[] PaymentKinds =
        [FinancialOperationKind.CreditCardPayment, FinancialOperationKind.LoanPayment];

    private readonly BankingDbContext _context;
    private readonly IBusinessClock _clock;

    public AdminRepository(BankingDbContext context, IBusinessClock clock) {
        _context = context;
        _clock = clock;
    }

    public async Task<OperationDashboardCounts> GetOperationCountsAsync(
        DateOnly businessDate,
        CancellationToken ct = default
    ) {
        (DateTimeOffset startUtc, DateTimeOffset endUtc) = BusinessDateRange.ToUtcRange(
            _clock.BusinessTimeZone,
            businessDate
        );

        IQueryable<FinancialOperation> operations = _context
            .Set<FinancialOperation>()
            .AsNoTracking();
        IQueryable<FinancialOperation> todayOperations = operations.Where(operation =>
            operation.OccurredAt >= startUtc && operation.OccurredAt < endUtc
        );

        // "Transacciones" cuenta todas las registradas (aprobadas y
        // rechazadas); "pagos" solo las aprobadas a tarjeta o préstamo
        // (spec §514-536).
        int totalTransactions = await operations.CountAsync(ct);
        int todayTransactions = await todayOperations.CountAsync(ct);

        int totalPayments = await operations.CountAsync(
            operation =>
                PaymentKinds.Contains(operation.Kind)
                && operation.Status == FinancialOperationStatus.Approved,
            ct
        );
        int todayPayments = await todayOperations.CountAsync(
            operation =>
                PaymentKinds.Contains(operation.Kind)
                && operation.Status == FinancialOperationStatus.Approved,
            ct
        );

        return new OperationDashboardCounts(
            totalTransactions,
            todayTransactions,
            totalPayments,
            todayPayments
        );
    }

    public Task<int> CountActiveLoansAsync(CancellationToken ct = default) =>
        _context
            .Set<Loan>()
            .AsNoTracking()
            .CountAsync(loan => loan.Status == LoanStatus.Active, ct);

    public Task<int> CountActiveCreditCardsAsync(CancellationToken ct = default) =>
        _context
            .Set<CreditCard>()
            .AsNoTracking()
            .CountAsync(card => card.Status == CreditCardStatus.Active, ct);

    public Task<int> CountActiveSavingsAccountsAsync(CancellationToken ct = default) =>
        _context
            .Set<SavingsAccount>()
            .AsNoTracking()
            .CountAsync(account => account.Status == AccountStatus.Active, ct);

    public async Task<Money> GetActiveClientDebtAsync(
        IReadOnlyCollection<string> activeClientIds,
        CancellationToken ct = default
    ) {
        if (activeClientIds.Count == 0) {
            return Money.Zero;
        }

        // El converter de Money impide sumar .Amount en SQL; se proyectan las
        // columnas necesarias y se suma en memoria (patrón de
        // CreditCardRepository), con el filtro de clientes activos en SQL.
        int[] activeLoanIds = await _context
            .Set<Loan>()
            .AsNoTracking()
            .Where(loan =>
                loan.Status == LoanStatus.Active
                && activeClientIds.Contains(loan.CustomerUserId)
            )
            .Select(loan => loan.Id)
            .ToArrayAsync(ct);

        decimal loanDebt = 0m;
        if (activeLoanIds.Length > 0) {
            decimal[] remainders = await _context
                .Set<Installment>()
                .AsNoTracking()
                .Where(installment => activeLoanIds.Contains(installment.LoanId))
                .Select(installment =>
                    installment.ScheduledAmount.Amount - installment.PaidAmount.Amount
                )
                .ToArrayAsync(ct);

            loanDebt = remainders.Sum();
        }

        decimal[] cardDebts = await _context
            .Set<CreditCard>()
            .AsNoTracking()
            .Where(card =>
                card.Status == CreditCardStatus.Active
                && activeClientIds.Contains(card.CustomerUserId)
            )
            .Select(card => card.CurrentDebt.Amount)
            .ToArrayAsync(ct);

        return Money.FromDecimal(loanDebt + cardDebts.Sum());
    }

    public async Task<IReadOnlyDictionary<string, ClientAssignmentFinancialFacts>>
        GetClientAssignmentFactsAsync(
        IReadOnlyCollection<string> clientIds,
        CancellationToken ct = default
    ) {
        Dictionary<string, ClientAssignmentFinancialFacts> facts = clientIds
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                clientId => clientId,
                _ => new ClientAssignmentFinancialFacts(
                    HasActiveLoan: false,
                    HasPrincipalSavingsAccount: false,
                    TotalDebt: 0m
                ),
                StringComparer.Ordinal
            );

        if (facts.Count == 0) {
            return facts;
        }

        // One set-based join for active loans and their installments. Money
        // value-converter columns are projected and grouped in memory because
        // EF Core cannot translate a converted value inside SQL GROUP BY/SUM.
        var loanRows = await (
            from loan in _context.Set<Loan>().AsNoTracking()
            join installment in _context.Set<Installment>().AsNoTracking()
                on loan.Id equals installment.LoanId
            where
                loan.Status == LoanStatus.Active
                && facts.Keys.Contains(loan.CustomerUserId)
            select new {
                loan.CustomerUserId,
                ScheduledAmount = installment.ScheduledAmount.Amount,
                PaidAmount = installment.PaidAmount.Amount,
            }
        )
            .ToListAsync(ct);

        var cardRows = await _context
            .Set<CreditCard>()
            .AsNoTracking()
            .Where(card =>
                card.Status == CreditCardStatus.Active && facts.Keys.Contains(card.CustomerUserId)
            )
            .Select(card => new {
                card.CustomerUserId,
                Debt = card.CurrentDebt.Amount,
            })
            .ToListAsync(ct);

        var principalOwners = await _context
            .Set<SavingsAccount>()
            .AsNoTracking()
            .Where(account =>
                account.Status == AccountStatus.Active
                && account.Type == AccountType.Primary
                && facts.Keys.Contains(account.OwnerUserId)
            )
            .Select(account => account.OwnerUserId)
            .Distinct()
            .ToListAsync(ct);

        Dictionary<string, decimal> loanDebts = loanRows
            .GroupBy(row => row.CustomerUserId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(row => row.ScheduledAmount - row.PaidAmount),
                StringComparer.Ordinal
            );
        Dictionary<string, decimal> cardDebts = cardRows
            .GroupBy(row => row.CustomerUserId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(row => row.Debt),
                StringComparer.Ordinal
            );
        HashSet<string> activeLoanOwnerIds = loanRows
            .Select(row => row.CustomerUserId)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> principalOwnerIds = principalOwners.ToHashSet(StringComparer.Ordinal);

        foreach (string clientId in facts.Keys.ToArray()) {
            facts[clientId] = new ClientAssignmentFinancialFacts(
                HasActiveLoan: activeLoanOwnerIds.Contains(clientId),
                HasPrincipalSavingsAccount: principalOwnerIds.Contains(clientId),
                TotalDebt:
                    loanDebts.GetValueOrDefault(clientId)
                    + cardDebts.GetValueOrDefault(clientId)
            );
        }

        return facts;
    }
}
