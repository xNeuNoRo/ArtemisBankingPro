using ArtemisBankingPro.Domain.Merchants.Entities;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

public interface IMerchantRepository : IGenericRepository<Merchant>
{
    Task<Merchant?> GetByRncAsync(string rnc, CancellationToken ct = default);

    Task<Merchant?> GetByAssociatedUserIdAsync(string userId, CancellationToken ct = default);
}
