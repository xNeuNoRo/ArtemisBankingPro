using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.Application.Features.FinancialProcessors;

/// <summary>
/// Procesa un avance de efectivo de forma atómica: calcula el cargo con la
/// política (6.25%), valida crédito disponible bajo concurrencia, persiste el
/// rechazo por crédito insuficiente cuando aplica, carga la tarjeta y acredita
/// la cuenta destino. No notifica ni conoce HTTP.
/// </summary>
public interface ICashAdvanceProcessor {
    Task<Result<FinancialOperationOutcome>> AdvanceAsync(
        CreditCardEntity card,
        SavingsAccount destinationAccount,
        Money principal,
        string initiatedByUserId,
        CancellationToken ct = default);
}
