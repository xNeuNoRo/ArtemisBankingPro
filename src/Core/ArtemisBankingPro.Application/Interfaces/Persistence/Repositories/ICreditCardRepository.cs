using ArtemisBankingPro.Domain.Cards.Entities;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

public interface ICreditCardRepository : IGenericRepository<CreditCard>
{
    Task<CreditCard?> GetByPanFingerprintAsync(
        string panFingerprint,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<CreditCard>> GetByCustomerAsync(
        string customerUserId,
        CancellationToken ct = default
    );
}
