using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
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
}
