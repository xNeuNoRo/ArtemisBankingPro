using ArtemisBankingPro.Domain.Common.Events;

namespace ArtemisBankingPro.Domain.Merchants.Events;

/// <summary>
/// Se desencadena cuando un comercio queda asociado a un usuario (rol Comercio).
/// </summary>
public sealed record MerchantUserAssociatedEvent(int MerchantId, string UserId) : IDomainEvent;
