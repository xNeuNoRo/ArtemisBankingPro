using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class CreditCardRepository : GenericRepository<CreditCard>, ICreditCardRepository
{
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
}
