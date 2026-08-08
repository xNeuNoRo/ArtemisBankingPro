using ArtemisBankingPro.Domain.Common.Events;

namespace ArtemisBankingPro.Domain.Cards.Events;

/// <summary>
/// Se desencadena cuando se asigna una tarjeta de crédito nueva. Nunca transporta
/// el PAN completo ni el CVC; solo los últimos cuatro dígitos y el límite.
/// </summary>
public sealed record CardAssignedEvent(
    string CustomerUserId,
    string LastFour,
    decimal CreditLimit,
    int ExpirationMonth,
    int ExpirationYear
) : IDomainEvent;
