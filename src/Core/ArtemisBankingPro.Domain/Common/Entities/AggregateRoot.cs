using ArtemisBankingPro.Domain.Common.Events;

namespace ArtemisBankingPro.Domain.Common.Entities;

/// <summary>
/// Marca un AggregateRoot con eventos de dominio en proceso.
/// </summary>
public interface IAggregateRoot {
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    internal void ClearDomainEvents();
}

/// <summary>
/// AggregateRoot: basicamente una entidad que puede desencadenar eventos de dominio en proceso.
/// Los eventos se despachan después de persistir, nunca dentro de la transacción.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
    where TId : notnull {
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent) {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    void IAggregateRoot.ClearDomainEvents() => _domainEvents.Clear();
}
