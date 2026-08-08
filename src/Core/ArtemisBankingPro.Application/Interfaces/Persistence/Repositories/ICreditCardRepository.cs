using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;

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

    /// <summary>Suma de la deuda de todas las tarjetas activas.</summary>
    Task<Money> GetTotalActiveDebtAsync(CancellationToken ct = default);

    /// <summary>Suma de la deuda de las tarjetas activas de un cliente.</summary>
    Task<Money> GetClientActiveDebtAsync(string customerUserId, CancellationToken ct = default);
}
