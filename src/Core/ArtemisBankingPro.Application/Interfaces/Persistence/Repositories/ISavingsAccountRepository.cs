using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

public interface ISavingsAccountRepository : IGenericRepository<SavingsAccount> {
    Task<PageResult<SavingsAccountSummaryDto>> GetPagedAsync(
        string? customerUserId,
        AccountStatus? status,
        AccountType? type,
        PageRequest page,
        CancellationToken ct = default
    );

    Task<PageResult<AccountTransactionDto>> GetTransactionsPagedAsync(
        AccountNumber accountNumber,
        PageRequest page,
        CancellationToken ct = default
    );

    Task<SavingsAccount?> GetByNumberAsync(AccountNumber number, CancellationToken ct = default);

    Task<SavingsAccount?> GetPrincipalByOwnerAsync(
        string ownerUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Cuenta de ahorro principal activa del usuario asociado al comercio
    /// (spec §41: el comercio recibe los pagos Hermes Pay en su cuenta
    /// principal). Devuelve <c>null</c> si el comercio no existe, no tiene
    /// usuario asociado o su usuario no tiene cuenta principal activa.
    /// </summary>
    Task<SavingsAccount?> GetPrincipalByCommerceIdAsync(
        int commerceId,
        CancellationToken ct = default
    );

    Task<bool> ExistsActivePrincipalAsync(string ownerUserId, CancellationToken ct = default);

    Task<IReadOnlyList<SavingsAccount>> GetByOwnerAsync(
        string ownerUserId,
        CancellationToken ct = default
    );
}
