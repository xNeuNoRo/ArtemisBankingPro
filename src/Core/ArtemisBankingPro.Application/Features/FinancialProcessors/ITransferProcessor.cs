using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Features.FinancialProcessors;

/// <summary>
/// Procesa una transferencia entre cuentas de forma atómica: revalida el
/// estado mutable, aplica el débito y el crédito en orden estable, crea las
/// transacciones pareadas y la operación. En los flujos del cliente persiste
/// el rechazo por fondos insuficientes en una transacción separada
/// (ADR-002). No notifica ni conoce HTTP.
/// </summary>
public interface ITransferProcessor {
    Task<Result<FinancialOperationOutcome>> TransferAsync(
        SavingsAccount source,
        SavingsAccount destination,
        Money amount,
        TransferFlow flow,
        string initiatedByUserId,
        CancellationToken ct = default);
}
