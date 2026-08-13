using ArtemisBankingPro.Domain.Common.Events;

namespace ArtemisBankingPro.Domain.Merchants.Events;

/// <summary>
/// Se desencadena cuando se actualiza la información de un comercio.
/// El estado del comercio no cambia en esta operación.
/// </summary>
public sealed record MerchantUpdatedEvent(int MerchantId, DateTimeOffset UpdatedAt)
    : IDomainEvent;
