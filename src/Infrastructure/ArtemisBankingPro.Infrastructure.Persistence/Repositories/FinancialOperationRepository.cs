using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class FinancialOperationRepository : IFinancialOperationRepository {
    private readonly BankingDbContext _context;

    public FinancialOperationRepository(BankingDbContext context) {
        _context = context;
    }

    public Task<FinancialOperation?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _context
            .Set<FinancialOperation>()
            .Include(operation => operation.AccountTransactions)
            .Include(operation => operation.CardConsumption)
            .AsSplitQuery()
            .FirstOrDefaultAsync(operation => operation.Id == id, ct);

    public Task<PageResult<FinancialOperation>> GetByAccountNumberAsync(
        AccountNumber accountNumber,
        PageRequest page,
        CancellationToken ct = default
    ) =>
        ToPageAsync(
            _context
                .Set<FinancialOperation>()
                .Where(operation =>
                    operation.AccountTransactions.Any(transaction =>
                        transaction.AccountNumber == accountNumber
                    )
                ),
            page,
            ct
        );

    public Task<PageResult<FinancialOperation>> GetByCardAsync(
        int creditCardId,
        PageRequest page,
        CancellationToken ct = default
    ) =>
        ToPageAsync(
            _context
                .Set<FinancialOperation>()
                .Where(operation =>
                    operation.CardConsumption != null
                    && operation.CardConsumption.CreditCardId == creditCardId
                ),
            page,
            ct
        );

    public Task<PageResult<FinancialOperation>> GetByLoanAsync(
        LoanNumber loanNumber,
        PageRequest page,
        CancellationToken ct = default
    ) =>
        ToPageAsync(
            _context
                .Set<FinancialOperation>()
                .Where(operation => operation.LoanNumber == loanNumber),
            page,
            ct
        );

    public Task<PageResult<FinancialOperation>> GetByInitiatorAsync(
        string initiatedByUserId,
        PageRequest page,
        CancellationToken ct = default
    ) =>
        ToPageAsync(
            _context
                .Set<FinancialOperation>()
                .Where(operation => operation.InitiatedByUserId == initiatedByUserId),
            page,
            ct
        );

    public async Task<FinancialOperation> AddAsync(
        FinancialOperation operation,
        CancellationToken ct = default
    ) {
        await _context.Set<FinancialOperation>().AddAsync(operation, ct);
        return operation;
    }

    private static async Task<PageResult<FinancialOperation>> ToPageAsync(
        IQueryable<FinancialOperation> query,
        PageRequest page,
        CancellationToken ct
    ) {
        int totalCount = await query.CountAsync(ct);
        List<FinancialOperation> items = await query
            .OrderByDescending(operation => operation.OccurredAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return new PageResult<FinancialOperation>(items, totalCount, page.Page, page.PageSize);
    }
}
