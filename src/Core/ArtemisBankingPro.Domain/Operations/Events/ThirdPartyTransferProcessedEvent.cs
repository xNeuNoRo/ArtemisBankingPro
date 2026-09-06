using ArtemisBankingPro.Domain.Common.Events;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Operations.Events;

/// <summary>
/// Se desencadena cuando una transferencia a cuentas de terceros (cajero) se
/// procesa y persiste atómicamente. Transporta solo datos seguros: números de
/// cuenta completos (identificadores no secretos), montos y propietarios.
/// Nunca transporta datos de tarjeta, CVC ni credenciales.
/// </summary>
public sealed record ThirdPartyTransferProcessedEvent(
    Guid OperationId,
    string SourceAccountNumber,
    string DestinationAccountNumber,
    Money Amount,
    string SourceOwnerUserId,
    string DestinationOwnerUserId,
    string CashierUserId,
    DateTimeOffset OccurredAt
) : IDomainEvent;
