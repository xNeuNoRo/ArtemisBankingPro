using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class MerchantRepository : GenericRepository<Merchant>, IMerchantRepository
{
    public MerchantRepository(BankingDbContext context)
        : base(context) { }

    public Task<Merchant?> GetByRncAsync(string rnc, CancellationToken ct = default) =>
        DbSet.FirstOrDefaultAsync(merchant => merchant.Rnc == rnc, ct);

    public Task<Merchant?> GetByAssociatedUserIdAsync(
        string userId,
        CancellationToken ct = default
    ) => DbSet.FirstOrDefaultAsync(merchant => merchant.AssociatedUserId == userId, ct);
}
