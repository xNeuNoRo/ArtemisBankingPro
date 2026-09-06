using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.Application.Features.FinancialProcessors;

/// <summary>
/// Procesa un pago a tarjeta de crédito de forma atómica: revalida el estado
/// mutable, capa el monto efectivo a la deuda real, débito de la cuenta,
/// reducción de deuda y operación <c>CreditCardPayment</c>. No notifica ni
/// conoce HTTP.
/// </summary>
public interface ICardPaymentProcessor {
    Task<Result<FinancialOperationOutcome>> PayAsync(
        CreditCardEntity card,
        SavingsAccount account,
        Money requestedAmount,
        string initiatedByUserId,
        CancellationToken ct = default);
}
