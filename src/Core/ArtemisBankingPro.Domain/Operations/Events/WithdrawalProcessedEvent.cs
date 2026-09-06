using ArtemisBankingPro.Domain.Common.Events;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Operations.Events;

/// <summary>
/// Se desencadena cuando un cajero procesa un retiro de una cuenta de ahorro.
/// Transporta solo datos seguros: operación, cuenta, monto, propietario y
/// responsable.
/// </summary>
public sealed record WithdrawalProcessedEvent(
    Guid OperationId,
    string AccountNumber,
    Money Amount,
    string OwnerUserId,
    string CashierUserId,
    DateTimeOffset OccurredAt
) : IDomainEvent;
