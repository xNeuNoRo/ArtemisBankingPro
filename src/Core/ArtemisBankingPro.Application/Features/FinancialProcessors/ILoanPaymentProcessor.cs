using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;

namespace ArtemisBankingPro.Application.Features.FinancialProcessors;

/// <summary>
/// Procesa un pago a préstamo de forma atómica: revalida el estado mutable,
/// capa el monto efectivo al pendiente real, débito de la cuenta, aplicación
/// a cuotas (parcial o multi-cuota) y operación <c>LoanPayment</c>. No
/// notifica ni conoce HTTP.
/// </summary>
public interface ILoanPaymentProcessor {
    Task<Result<FinancialOperationOutcome>> PayAsync(
        Loan loan,
        SavingsAccount account,
        Money requestedAmount,
        string initiatedByUserId,
        CancellationToken ct = default);
}
