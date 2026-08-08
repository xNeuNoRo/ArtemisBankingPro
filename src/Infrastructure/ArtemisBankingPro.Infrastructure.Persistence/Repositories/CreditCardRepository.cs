using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class CreditCardRepository : GenericRepository<CreditCard>, ICreditCardRepository {
    public CreditCardRepository(BankingDbContext context)
        : base(context) { }

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
