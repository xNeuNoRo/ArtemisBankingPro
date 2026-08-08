using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

public interface ISavingsAccountRepository : IGenericRepository<SavingsAccount> {
    Task<SavingsAccount?> GetByNumberAsync(AccountNumber number, CancellationToken ct = default);

    Task<SavingsAccount?> GetPrincipalByOwnerAsync(
        string ownerUserId,
        CancellationToken ct = default
    );

    Task<bool> ExistsActivePrincipalAsync(string ownerUserId, CancellationToken ct = default);

    Task<IReadOnlyList<SavingsAccount>> GetByOwnerAsync(
        string ownerUserId,
        CancellationToken ct = default
    );
}
