using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

/// <summary>
/// Repositorio del historial financiero (solo lectura y alta, nunca borrado).
/// </summary>
public interface IFinancialOperationRepository
{
    Task<FinancialOperation?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PageResult<FinancialOperation>> GetByAccountNumberAsync(
        AccountNumber accountNumber,
        PageRequest page,
        CancellationToken ct = default
    );

    Task<PageResult<FinancialOperation>> GetByCardAsync(
        int creditCardId,
        PageRequest page,
        CancellationToken ct = default
    );

    Task<PageResult<FinancialOperation>> GetByLoanAsync(
        LoanNumber loanNumber,
        PageRequest page,
        CancellationToken ct = default
    );

    Task<PageResult<FinancialOperation>> GetByInitiatorAsync(
        string initiatedByUserId,
        PageRequest page,
        CancellationToken ct = default
    );

    Task<FinancialOperation> AddAsync(FinancialOperation operation, CancellationToken ct = default);
}
