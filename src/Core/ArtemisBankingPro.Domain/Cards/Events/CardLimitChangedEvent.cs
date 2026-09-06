using ArtemisBankingPro.Domain.Common.Events;

namespace ArtemisBankingPro.Domain.Cards.Events;

/// <summary>
/// Se desencadena cuando cambia el límite de crédito de una tarjeta activa.
/// </summary>
public sealed record CardLimitChangedEvent(
    string CustomerUserId,
    string LastFour,
    decimal NewCreditLimit
) : IDomainEvent;
