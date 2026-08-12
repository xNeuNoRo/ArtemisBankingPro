using ArtemisBankingPro.Domain.Common.Events;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Operations.Events;

/// <summary>
/// Se desencadena cuando un pago Hermes Pay es procesado exitosamente a favor
/// de un comercio. Transporta solo datos seguros: operación, últimos cuatro
/// dígitos de la tarjeta, comercio, monto, propietario de la tarjeta y correo
/// del comercio para las notificaciones post-commit.
/// </summary>
public sealed record HermesPayProcessedEvent(
    Guid OperationId,
    string CardLastFour,
    int MerchantId,
    string MerchantName,
    Money Amount,
    string CardOwnerUserId,
    string MerchantEmail,
    DateTimeOffset OccurredAt
) : IDomainEvent;
