using ArtemisBankingPro.Domain.Common.Events;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Cards.Events;

/// <summary>
/// Se desencadena cuando se aplica un pago a una tarjeta de crédito activa.
/// Transporta solo datos seguros: identificación de la tarjeta (últimos cuatro
/// dígitos), monto aplicado (efectivo, ya capado a la deuda real), deuda
/// restante y propietario.
/// </summary>
public sealed record CardPaymentProcessedEvent(
    string CustomerUserId,
    string LastFour,
    Money AppliedAmount,
    Money RemainingDebt,
    DateTimeOffset PaidAt
) : IDomainEvent;
