using ArtemisBankingPro.Domain.Common.Events;

namespace ArtemisBankingPro.Domain.Merchants.Events;

/// <summary>
/// Se desencadena cuando cambia el estado de un comercio (activado/inactivado).
/// Un comercio inactivo no procesa pagos Hermes Pay (regla aplicada en WIHP-01).
/// </summary>
public sealed record MerchantStatusChangedEvent(
    int MerchantId,
    bool IsActive,
    DateTimeOffset ChangedAt
) : IDomainEvent;
