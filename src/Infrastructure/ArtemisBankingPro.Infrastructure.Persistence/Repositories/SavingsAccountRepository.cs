using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class SavingsAccountRepository
    : GenericRepository<SavingsAccount>,
        ISavingsAccountRepository
{
    public SavingsAccountRepository(BankingDbContext context)
        : base(context) { }

    public Task<SavingsAccount?> GetByNumberAsync(
        AccountNumber number,
        CancellationToken ct = default
    ) => DbSet.FirstOrDefaultAsync(account => account.Number == number, ct);

    public Task<SavingsAccount?> GetPrincipalByOwnerAsync(
        string ownerUserId,
        CancellationToken ct = default
    ) =>
        DbSet.FirstOrDefaultAsync(
            account =>
                account.OwnerUserId == ownerUserId
                && account.Type == AccountType.Primary
                && account.Status == AccountStatus.Active,
            ct
        );

    public Task<bool> ExistsActivePrincipalAsync(
        string ownerUserId,
        CancellationToken ct = default
    ) =>
        DbSet.AnyAsync(
            account =>
                account.OwnerUserId == ownerUserId
                && account.Type == AccountType.Primary
                && account.Status == AccountStatus.Active,
            ct
        );

    public async Task<IReadOnlyList<SavingsAccount>> GetByOwnerAsync(
        string ownerUserId,
        CancellationToken ct = default
    ) =>
        await DbSet
            .AsNoTracking()
            .Where(account => account.OwnerUserId == ownerUserId)
            .OrderByDescending(account => account.Id)
            .ToListAsync(ct);
}
