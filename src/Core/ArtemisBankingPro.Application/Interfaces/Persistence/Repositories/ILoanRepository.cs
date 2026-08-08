using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using ArtemisBankingPro.Domain.Lending.ValueObjects;

namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

public interface ILoanRepository : IGenericRepository<Loan>
{
    Task<Loan?> GetByNumberAsync(LoanNumber number, CancellationToken ct = default);

    Task<Loan?> GetActiveByCustomerAsync(string customerUserId, CancellationToken ct = default);

    Task<Loan?> GetWithInstallmentsByIdAsync(int loanId, CancellationToken ct = default);

    Task<IReadOnlyList<Installment>> GetPendingInstallmentsAsync(
        int loanId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Listado paginado de préstamos con filtro por estado y cliente.
    /// </summary>
    Task<PageResult<Loan>> GetPagedAsync(
        string? customerUserId,
        LoanStatus? status,
        PageRequest page,
        CancellationToken ct = default
    );

    /// <summary>Suma del monto pendiente de todos los préstamos activos.</summary>
    Task<Money> GetTotalActiveDebtAsync(CancellationToken ct = default);

    /// <summary>Suma del monto pendiente de los préstamos activos de un cliente.</summary>
    Task<Money> GetClientActiveDebtAsync(string customerUserId, CancellationToken ct = default);
}
