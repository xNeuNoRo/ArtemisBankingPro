using ArtemisBankingPro.Domain.Common.Events;

namespace ArtemisBankingPro.Application.Interfaces.Events;

/// <summary>
/// Despacha eventos de dominio en proceso, después de persistir.
/// Los handlers se registran como <see cref="IEventHandler{TEvent}"/>.
/// </summary>
public interface IDomainEventDispatcher {
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct = default);
}

public interface IEventHandler<in TEvent>
    where TEvent : IDomainEvent {
    Task HandleAsync(TEvent domainEvent, CancellationToken ct = default);
}
