using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class LoanRepository : GenericRepository<Loan>, ILoanRepository {
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

    public async Task<IReadOnlyList<int>> GetActivePastDueLoanIdsAsync(
        DateOnly businessDate,
        int afterLoanId,
        int batchSize,
        CancellationToken ct = default
    ) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        return await DbSet
            .AsNoTracking()
            .Where(loan =>
                loan.Id > afterLoanId
                && loan.Status == LoanStatus.Active
                && loan.Installments.Any(installment =>
                    installment.DueDate < businessDate
                    && installment.PaidAmount != installment.ScheduledAmount
                    && !installment.IsOverdue
                )
            )
            .OrderBy(loan => loan.Id)
            .Select(loan => loan.Id)
            .Take(batchSize)
            .ToListAsync(ct);
    }

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
    ) {
        IQueryable<Loan> query = DbSet
            .AsNoTracking()
            .Include(loan => loan.Installments);

        if (!string.IsNullOrWhiteSpace(customerUserId)) {
            query = query.Where(loan => loan.CustomerUserId == customerUserId);
        }

        if (status is null) {
            query = query.OrderByDescending(loan => loan.Status == LoanStatus.Active)
                .ThenByDescending(loan => loan.IssuedAt)
                .ThenByDescending(loan => loan.Id);
        }
        else {
            query = query
                .Where(loan => loan.Status == status)
                .OrderByDescending(loan => loan.IssuedAt)
                .ThenByDescending(loan => loan.Id);
        }

        int totalCount = await query.CountAsync(ct);
        List<Loan> items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);

        return new PageResult<Loan>(items, totalCount, page.Page, page.PageSize);
    }

    public async Task<Money> GetTotalActiveDebtAsync(CancellationToken ct = default) {
        return await GetActiveDebtAsync(customerUserId: null, ct: ct);
    }

    public async Task<Money> GetClientActiveDebtAsync(
        string customerUserId,
        CancellationToken ct = default
    ) {
        return await GetActiveDebtAsync(customerUserId, ct);
    }

    private async Task<Money> GetActiveDebtAsync(
        string? customerUserId,
        CancellationToken ct
    ) {
        IQueryable<Installment> installments = Context
            .Loans
            .AsNoTracking()
            .Where(loan => loan.Status == LoanStatus.Active)
            .Where(loan => customerUserId == null || loan.CustomerUserId == customerUserId)
            .SelectMany(loan => loan.Installments);

        var amounts = await installments
            .Select(installment => new {
                installment.ScheduledAmount,
                installment.PaidAmount,
            })
            .ToListAsync(ct);
        decimal debt = amounts.Sum(item => item.ScheduledAmount.Amount - item.PaidAmount.Amount);
        return Money.FromDecimal(debt);
    }
}
