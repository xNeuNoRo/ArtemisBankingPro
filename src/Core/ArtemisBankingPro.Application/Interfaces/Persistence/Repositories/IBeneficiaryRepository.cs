using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

public interface IBeneficiaryRepository : IGenericRepository<Beneficiary> {
    Task<IReadOnlyList<Beneficiary>> GetByOwnerAsync(
        string ownerUserId,
        CancellationToken ct = default
    );

    Task<bool> ExistsAsync(
        string ownerUserId,
        int destinationAccountId,
        CancellationToken ct = default
    );

    void Delete(Beneficiary beneficiary);
}
