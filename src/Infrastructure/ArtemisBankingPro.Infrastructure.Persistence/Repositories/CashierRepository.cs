using ArtemisBankingPro.Application.Common.Time;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

/// <summary>
/// Consultas de lectura del módulo de cajero sobre el historial financiero.
/// Las fechas de negocio se convierten a rangos UTC con la zona empresarial
/// configurada.
/// </summary>
public sealed class CashierRepository : ICashierRepository {
    private static readonly FinancialOperationKind[] PaymentKinds =
        [FinancialOperationKind.CreditCardPayment, FinancialOperationKind.LoanPayment];

    private readonly BankingDbContext _context;
    private readonly IBusinessClock _clock;

    public CashierRepository(BankingDbContext context, IBusinessClock clock) {
        _context = context;
        _clock = clock;
    }

    public async Task<CashierDashboardDto> GetDashboardAsync(
        string cashierId,
        DateOnly date,
        CancellationToken ct = default
    ) {
        (DateTimeOffset startUtc, DateTimeOffset endUtc) = BusinessDateRange.ToUtcRange(
            _clock.BusinessTimeZone,
            date
        );

        IQueryable<FinancialOperation> dayOperations = DbSet
            .AsNoTracking()
            .Where(operation =>
                operation.InitiatedByUserId == cashierId
                && operation.OccurredAt >= startUtc
                && operation.OccurredAt < endUtc
            );

        // Indicadores exclusivos del cajero autenticado; agregados con COUNT
        // en SQL, sin materializar filas.
        int transactionsToday = await dayOperations.CountAsync(ct);

        int depositsToday = await dayOperations.CountAsync(
            operation =>
                operation.Kind == FinancialOperationKind.Deposit
                && operation.Status == FinancialOperationStatus.Approved,
            ct
        );

        int withdrawalsToday = await dayOperations.CountAsync(
            operation =>
                operation.Kind == FinancialOperationKind.Withdrawal
                && operation.Status == FinancialOperationStatus.Approved,
            ct
        );

        // Solo los pagos aprobados a tarjeta y a préstamo cuentan; los
        // rechazados no modifican deuda ni montos.
        int paymentsToday = await dayOperations.CountAsync(
            operation =>
                PaymentKinds.Contains(operation.Kind)
                && operation.Status == FinancialOperationStatus.Approved,
            ct
        );

        return new CashierDashboardDto(
            transactionsToday,
            paymentsToday,
            depositsToday,
            withdrawalsToday
        );
    }

    public async Task<PageResult<CashierOperationDto>> GetOperationsPagedAsync(
        string cashierId,
        CashierOperationFilters filters,
        PageRequest page,
        CancellationToken ct = default
    ) {
        IQueryable<FinancialOperation> query = DbSet
            .AsNoTracking()
            .Where(operation => operation.InitiatedByUserId == cashierId);

        if (filters.Kind is not null) {
            query = query.Where(operation => operation.Kind == filters.Kind);
        }

        if (filters.DateFrom is { } dateFrom) {
            query = query.Where(operation => operation.OccurredAt >= dateFrom);
        }

        if (filters.DateTo is { } dateTo) {
            query = query.Where(operation => operation.OccurredAt <= dateTo);
        }

        int totalCount = await query.CountAsync(ct);

        // Solo columnas necesarias; la cuenta asociada se toma con orden
        // determinista (débito primero) para listar una sola cuenta por
        // operación.
        var rows = await query
            .OrderByDescending(operation => operation.OccurredAt)
            .ThenByDescending(operation => operation.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(operation => new {
                operation.Id,
                operation.Kind,
                operation.Status,
                operation.RequestedAmount,
                operation.AppliedAmount,
                operation.OccurredAt,
                operation.RejectionCode,
                operation.CreditCardId,
                operation.LoanNumber,
                AccountNumber = operation
                    .AccountTransactions.OrderBy(transaction =>
                        transaction.Direction == TransactionDirection.Debit ? 0 : 1
                    )
                    .ThenBy(transaction => transaction.AccountNumber)
                    .Select(transaction => transaction.AccountNumber)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        int[] cardIds = rows
            .Where(row => row.CreditCardId is not null)
            .Select(row => (int)row.CreditCardId!)
            .Distinct()
            .ToArray();

        // Últimos 4 dígitos de las tarjetas referenciadas, sin exponer el PAN.
        Dictionary<int, string> cardLastFours = cardIds.Length == 0
            ? new Dictionary<int, string>()
            : await _context
                .Set<CreditCard>()
                .AsNoTracking()
                .Where(card => cardIds.Contains(card.Id))
                .ToDictionaryAsync(card => card.Id, card => card.LastFour, ct);

        List<CashierOperationDto> items = rows
            .Select(row => new CashierOperationDto(
                row.Id,
                row.Kind.ToString(),
                row.Status.ToString(),
                AmountForDisplay(row.Status, row.RequestedAmount, row.AppliedAmount),
                row.OccurredAt,
                row.AccountNumber is null ? null : row.AccountNumber.Value[^4..],
                row.CreditCardId is null
                    ? null
                    : cardLastFours.GetValueOrDefault(row.CreditCardId.Value),
                row.LoanNumber?.Value,
                row.RejectionCode
            ))
            .ToList();

        return new PageResult<CashierOperationDto>(items, totalCount, page.Page, page.PageSize);
    }

    private DbSet<FinancialOperation> DbSet => _context.Set<FinancialOperation>();

    private static decimal AmountForDisplay(
        FinancialOperationStatus status,
        Money requestedAmount,
        Money appliedAmount
    ) =>
        status == FinancialOperationStatus.Approved ? appliedAmount.Amount : requestedAmount.Amount;
}
