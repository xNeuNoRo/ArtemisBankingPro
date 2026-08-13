using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class MerchantRepository : GenericRepository<Merchant>, IMerchantRepository {
    public MerchantRepository(BankingDbContext context)
        : base(context) { }

    public Task<Merchant?> GetByRncAsync(string rnc, CancellationToken ct = default) =>
        DbSet.FirstOrDefaultAsync(merchant => merchant.Rnc == rnc, ct);

    public Task<bool> ExistsByRncAsync(string rnc, CancellationToken ct = default) =>
        DbSet.AnyAsync(merchant => merchant.Rnc == rnc, ct);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default) =>
        DbSet.AnyAsync(merchant => merchant.Email == email, ct);

    public Task<Merchant?> GetByAssociatedUserIdAsync(
        string userId,
        CancellationToken ct = default
    ) => DbSet.FirstOrDefaultAsync(merchant => merchant.AssociatedUserId == userId, ct);

    public async Task<PageResult<MerchantSummaryDto>> GetPagedAsync(
        MerchantStatus? status,
        PageRequest page,
        CancellationToken ct = default
    ) {
        IQueryable<Merchant> query = DbSet.AsNoTracking();

        if (status is not null) {
            query = query.Where(merchant => merchant.Status == status.Value);
        }

        int totalCount = await query.CountAsync(ct);

        // CreatedAt es una propiedad mapeada con setter privado; se proyecta
        // explícitamente sin materializar la entidad completa.
        var rows = await query
            .OrderByDescending(merchant => EF.Property<DateTimeOffset>(merchant, "CreatedAt"))
            .ThenByDescending(merchant => merchant.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(merchant => new {
                merchant.Id,
                merchant.Name,
                merchant.Description,
                merchant.Email,
                merchant.PhoneNumber,
                merchant.Rnc,
                merchant.Status,
                merchant.AssociatedUserId,
                CreatedAt = EF.Property<DateTimeOffset>(merchant, "CreatedAt"),
            })
            .ToListAsync(ct);

        List<MerchantSummaryDto> items = rows
            .Select(merchant => new MerchantSummaryDto(
                merchant.Id,
                merchant.Name,
                merchant.Description,
                merchant.Email,
                merchant.PhoneNumber,
                merchant.Rnc,
                merchant.Status == MerchantStatus.Active,
                merchant.AssociatedUserId is not null,
                merchant.CreatedAt
            ))
            .ToList();

        return new PageResult<MerchantSummaryDto>(items, totalCount, page.Page, page.PageSize);
    }
}
