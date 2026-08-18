using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Features.FinancialProcessors;

/// <summary>
/// Procesa un retiro de forma atómica: revalida el estado mutable de la
/// cuenta, verifica fondos y ejecuta el débito más la operación
/// <c>Withdrawal</c>. Cuando los fondos son insuficientes persiste el intento
/// como <c>Rejected</c> sin modificar el balance. No notifica ni conoce HTTP.
/// </summary>
public interface IWithdrawalProcessor {
    Task<Result<FinancialOperationOutcome>> WithdrawAsync(
        SavingsAccount account,
        Money amount,
        string initiatedByUserId,
        CancellationToken ct = default);
}
