using ArtemisBankingPro.Domain.Common.Events;

namespace ArtemisBankingPro.Domain.Merchants.Events;

/// <summary>
/// Se desencadena cuando se crea un comercio en estado Activo.
/// El identificador se genera al persistir; la correlación se hace por RNC.
/// </summary>
public sealed record MerchantCreatedEvent(
    string Name,
    string Rnc,
    DateTimeOffset CreatedAt
) : IDomainEvent;
