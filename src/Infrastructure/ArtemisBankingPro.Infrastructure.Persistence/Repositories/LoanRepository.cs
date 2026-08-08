using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class LoanRepository : GenericRepository<Loan>, ILoanRepository
{
    public LoanRepository(BankingDbContext context)
        : base(context) { }

    public Task<Loan?> GetByNumberAsync(LoanNumber number, CancellationToken ct = default) =>
        DbSet.FirstOrDefaultAsync(loan => loan.Number == number, ct);

    public Task<Loan?> GetActiveByCustomerAsync(
        string customerUserId,
        CancellationToken ct = default
    ) =>
        DbSet.FirstOrDefaultAsync(
            loan => loan.CustomerUserId == customerUserId && loan.Status == LoanStatus.Active,
            ct
        );

    public Task<Loan?> GetWithInstallmentsByIdAsync(int loanId, CancellationToken ct = default) =>
        DbSet.Include(loan => loan.Installments).FirstOrDefaultAsync(loan => loan.Id == loanId, ct);

    public async Task<IReadOnlyList<Installment>> GetPendingInstallmentsAsync(
        int loanId,
        CancellationToken ct = default
    ) =>
        await Context
            .Set<Installment>()
            .AsNoTracking()
            .Where(installment => installment.LoanId == loanId)
            .Where(installment => installment.PaidAmount != installment.ScheduledAmount)
            .OrderBy(installment => installment.Number)
            .ToListAsync(ct);

    public async Task<PageResult<Loan>> GetPagedAsync(
        string? customerUserId,
        LoanStatus? status,
        PageRequest page,
        CancellationToken ct = default
    )
    {
        IQueryable<Loan> query = DbSet.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(customerUserId))
        {
            query = query.Where(loan => loan.CustomerUserId == customerUserId);
        }

        if (status is null)
        {
            query = query.OrderByDescending(loan => loan.Status == LoanStatus.Active)
                .ThenByDescending(loan => loan.IssuedAt);
        }
        else
        {
            query = query
                .Where(loan => loan.Status == status)
                .OrderByDescending(loan => loan.IssuedAt);
        }

        int totalCount = await query.CountAsync(ct);
        List<Loan> items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);

        return new PageResult<Loan>(items, totalCount, page.Page, page.PageSize);
    }

    public async Task<Money> GetTotalActiveDebtAsync(CancellationToken ct = default)
    {
        int[] activeLoanIds = await Context
            .Set<Loan>()
            .AsNoTracking()
            .Where(loan => loan.Status == LoanStatus.Active)
            .Select(loan => loan.Id)
            .ToArrayAsync(ct);

        if (activeLoanIds.Length == 0)
        {
            return Money.Zero;
        }

        decimal[] remainders = await Context
            .Set<Installment>()
            .Where(installment => activeLoanIds.Contains(installment.LoanId))
            .Select(installment =>
                installment.ScheduledAmount.Amount - installment.PaidAmount.Amount
            )
            .ToArrayAsync(ct);

        return Money.FromDecimal(remainders.Sum());
    }

    public async Task<Money> GetClientActiveDebtAsync(
        string customerUserId,
        CancellationToken ct = default
    )
    {
        int[] activeLoanIds = await Context
            .Set<Loan>()
            .AsNoTracking()
            .Where(loan =>
                loan.CustomerUserId == customerUserId && loan.Status == LoanStatus.Active
            )
            .Select(loan => loan.Id)
            .ToArrayAsync(ct);

        if (activeLoanIds.Length == 0)
        {
            return Money.Zero;
        }

        decimal[] remainders = await Context
            .Set<Installment>()
            .Where(installment => activeLoanIds.Contains(installment.LoanId))
            .Select(installment =>
                installment.ScheduledAmount.Amount - installment.PaidAmount.Amount
            )
            .ToArrayAsync(ct);

        return Money.FromDecimal(remainders.Sum());
    }
}
