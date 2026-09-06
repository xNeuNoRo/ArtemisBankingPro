using ArtemisBankingPro.Domain.Common.Events;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.Events;

/// <summary>
/// Se desencadena cuando se aplica un pago a un préstamo activo. Transporta
/// solo datos seguros: identificación del préstamo, monto aplicado (efectivo,
/// ya capado al pendiente real), saldo restante y propietario.
/// </summary>
public sealed record LoanPaymentProcessedEvent(
    string CustomerUserId,
    LoanNumber LoanNumber,
    Money AppliedAmount,
    Money RemainingOutstandingAmount,
    DateTimeOffset PaidAt
) : IDomainEvent;
