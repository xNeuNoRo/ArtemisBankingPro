using ArtemisBankingPro.Domain.Common.Events;

namespace ArtemisBankingPro.Domain.Cards.Events;

/// <summary>
/// Se desencadena cuando se cancela una tarjeta de crédito activa sin deuda.
/// </summary>
public sealed record CardCancelledEvent(
    string CustomerUserId,
    string LastFour,
    DateTimeOffset CancelledAt
) : IDomainEvent;
