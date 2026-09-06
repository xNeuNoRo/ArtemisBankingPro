using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class SavingsAccountRepository
    : GenericRepository<SavingsAccount>,
        ISavingsAccountRepository {
    public SavingsAccountRepository(BankingDbContext context)
        : base(context) { }

    public async Task<PageResult<SavingsAccountSummaryDto>> GetPagedAsync(
        string? customerUserId,
        AccountStatus? status,
        AccountType? type,
        PageRequest page,
        CancellationToken ct = default
    ) {
        IQueryable<SavingsAccount> query = DbSet.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(customerUserId)) {
            query = query.Where(account => account.OwnerUserId == customerUserId);
        }

        if (status is not null) {
            query = query.Where(account => account.Status == status);
        }

        if (type is not null) {
            query = query.Where(account => account.Type == type);
        }

        query = query
            .OrderByDescending(account => account.Status == AccountStatus.Active)
            .ThenByDescending(account => account.OpenedAt)
            .ThenByDescending(account => account.Id);

        int totalCount = await query.CountAsync(ct);
        var rows = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(account => new {
                account.Id,
                account.Number,
                account.OwnerUserId,
                account.Balance,
                account.Type,
                account.Status,
                account.OpenedAt,
            })
            .ToListAsync(ct);
        List<SavingsAccountSummaryDto> items = rows
            .Select(account =>
                new SavingsAccountSummaryDto(
                    account.Id,
                    account.Number.Value,
                    account.OwnerUserId,
                    string.Empty,
                    string.Empty,
                    account.Balance.Amount,
                    account.Type.ToString(),
                    account.Status.ToString(),
                    account.OpenedAt
                )
            )
            .ToList();

        return new PageResult<SavingsAccountSummaryDto>(items, totalCount, page.Page, page.PageSize);
    }

    public async Task<PageResult<AccountTransactionDto>> GetTransactionsPagedAsync(
        AccountNumber accountNumber,
        PageRequest page,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? transactionType = null,
        CancellationToken ct = default
    ) {
        TransactionDirection? direction = null;
        if (!string.IsNullOrWhiteSpace(transactionType)) {
            direction = transactionType.Trim().ToUpperInvariant() switch {
                "DÉBITO" or "DEBITO" => TransactionDirection.Debit,
                "CRÉDITO" or "CREDITO" => TransactionDirection.Credit,
                _ => null,
            };
        }

        if (direction is null && !string.IsNullOrWhiteSpace(transactionType)) {
            return new PageResult<AccountTransactionDto>([], 0, page.Page, page.PageSize);
        }

        IQueryable<AccountTransaction> query = Context
            .Set<AccountTransaction>()
            .AsNoTracking()
            .Where(transaction => transaction.AccountNumber == accountNumber);
        if (direction.HasValue) {
            query = query.Where(transaction => transaction.Direction == direction.Value);
        }

        var joined = query.Join(
            Context.Set<FinancialOperation>(),
            transaction => transaction.FinancialOperationId,
            operation => operation.Id,
            (transaction, operation) => new { transaction, operation }
        );
        if (dateFrom.HasValue) {
            joined = joined.Where(item => item.operation.OccurredAt >= dateFrom.Value);
        }

        if (dateTo.HasValue) {
            joined = joined.Where(item => item.operation.OccurredAt <= dateTo.Value);
        }

        int totalCount = await joined.CountAsync(ct);
        List<AccountTransactionDto> items = await joined
            .OrderByDescending(item => item.operation.OccurredAt)
            .ThenByDescending(item => item.transaction.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(item =>
                new AccountTransactionDto(
                    item.transaction.Id,
                    item.operation.OccurredAt,
                    item.transaction.Amount.Amount,
                    item.transaction.Direction == TransactionDirection.Credit
                        ? "CRÉDITO"
                        : "DÉBITO",
                    item.transaction.OriginReference,
                    item.transaction.BeneficiaryReference,
                    item.operation.Status == FinancialOperationStatus.Approved
                        ? "APROBADA"
                        : "RECHAZADA"
                )
            )
            .ToListAsync(ct);

        return new PageResult<AccountTransactionDto>(items, totalCount, page.Page, page.PageSize);
    }

    public Task<SavingsAccount?> GetByNumberAsync(
        AccountNumber number,
        CancellationToken ct = default
    ) => DbSet.FirstOrDefaultAsync(account => account.Number == number, ct);

    public async Task<IReadOnlyList<SavingsAccount>> GetByIdsAsync(
        IReadOnlyList<int> ids,
        CancellationToken ct = default
    ) =>
        await DbSet
            .AsNoTracking()
            .Where(account => ids.Contains(account.Id))
            .ToListAsync(ct);

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

    public Task<SavingsAccount?> GetPrincipalByCommerceIdAsync(
        int commerceId,
        CancellationToken ct = default
    ) =>
        DbSet.FirstOrDefaultAsync(
            account =>
                account.Type == AccountType.Primary
                && account.Status == AccountStatus.Active
                && Context
                    .Set<Merchant>()
                    .Any(merchant =>
                        merchant.Id == commerceId
                        && merchant.AssociatedUserId == account.OwnerUserId
                    ),
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

    public Task<int> CountActiveByOwnerAsync(
        string ownerUserId,
        CancellationToken ct = default
    ) =>
        DbSet.CountAsync(
            account =>
                account.OwnerUserId == ownerUserId && account.Status == AccountStatus.Active,
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
