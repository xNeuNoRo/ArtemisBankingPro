using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class CreditCardRepository : GenericRepository<CreditCard>, ICreditCardRepository {
    public CreditCardRepository(BankingDbContext context)
        : base(context) { }

    public async Task<PageResult<CardConsumptionView>> GetConsumptionsPagedAsync(
        int creditCardId,
        PageRequest page,
        CancellationToken ct = default
    ) {
        IQueryable<CardConsumption> query = Context
            .Set<CardConsumption>()
            .AsNoTracking()
            .Where(consumption => consumption.CreditCardId == creditCardId);

        int totalCount = await query.CountAsync(ct);

        List<CardConsumptionView> items = await query
            .Join(
                Context.Set<FinancialOperation>(),
                consumption => consumption.FinancialOperationId,
                operation => operation.Id,
                (consumption, operation) => new { consumption, operation }
            )
            .OrderByDescending(item => item.operation.OccurredAt)
            .ThenByDescending(item => item.consumption.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(item => new CardConsumptionView(
                item.consumption.Id,
                item.operation.OccurredAt,
                item.consumption.Amount.Amount,
                item.consumption.Type == ConsumptionType.CashAdvance
                    ? "AVANCE"
                    : item.consumption.MerchantDisplayName,
                item.operation.Status
            ))
            .ToListAsync(ct);

        return new PageResult<CardConsumptionView>(items, totalCount, page.Page, page.PageSize);
    }

    public Task<CreditCard?> GetByPanFingerprintAsync(
        string panFingerprint,
        CancellationToken ct = default
    ) => DbSet.FirstOrDefaultAsync(card => card.PanFingerprint == panFingerprint, ct);

    public async Task<IReadOnlyList<CreditCard>> GetByCustomerAsync(
        string customerUserId,
        CancellationToken ct = default
    ) =>
        await DbSet
            .AsNoTracking()
            .Where(card => card.CustomerUserId == customerUserId)
            .OrderByDescending(card => card.Id)
            .ToListAsync(ct);

    public async Task<Money> GetTotalActiveDebtAsync(CancellationToken ct = default) {
        // El converter de Money impide navegar .Amount dentro de Sum en SQL
        // materializar solo las columnas necesarias y sumar en memoria es
        // correcto para el volumen de tarjetas activas.
        decimal[] debts = await DbSet
            .AsNoTracking()
            .Where(card => card.Status == CreditCardStatus.Active)
            .Select(card => card.CurrentDebt.Amount)
            .ToArrayAsync(ct);

        return Money.FromDecimal(debts.Sum());
    }

    public async Task<Money> GetClientActiveDebtAsync(
        string customerUserId,
        CancellationToken ct = default
    ) {
        decimal[] debts = await DbSet
            .AsNoTracking()
            .Where(card =>
                card.CustomerUserId == customerUserId && card.Status == CreditCardStatus.Active
            )
            .Select(card => card.CurrentDebt.Amount)
            .ToArrayAsync(ct);

        return Money.FromDecimal(debts.Sum());
    }
}
