using ArtemisBankingPro.Domain.Lending.Entities;
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
}
