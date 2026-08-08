using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class BeneficiaryRepository : GenericRepository<Beneficiary>, IBeneficiaryRepository
{
    public BeneficiaryRepository(BankingDbContext context)
        : base(context) { }

    public async Task<IReadOnlyList<Beneficiary>> GetByOwnerAsync(
        string ownerUserId,
        CancellationToken ct = default
    ) =>
        await DbSet
            .Where(beneficiary => beneficiary.OwnerUserId == ownerUserId)
            .OrderBy(beneficiary => beneficiary.DestinationAccountId)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(
        string ownerUserId,
        int destinationAccountId,
        CancellationToken ct = default
    ) =>
        DbSet.AnyAsync(
            beneficiary =>
                beneficiary.OwnerUserId == ownerUserId
                && beneficiary.DestinationAccountId == destinationAccountId,
            ct
        );
}
