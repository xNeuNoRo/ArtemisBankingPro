using System.Reflection;
using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Domain.Common.Events;
using Microsoft.Extensions.DependencyInjection;

namespace ArtemisBankingPro.Infrastructure.Persistence.Events;

/// <summary>
/// Despacha cada evento de dominio a todos los handlers registrados
/// (<see cref="IEventHandler{TEvent}"/>) del tipo concreto del evento.
/// </summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _provider;

    public DomainEventDispatcher(IServiceProvider provider)
    {
        _provider = provider;
    }

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct = default)
    {
        Type eventType = domainEvent.GetType();
        Type handlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
        MethodInfo handleMethod = handlerType.GetMethod(
            nameof(IEventHandler<IDomainEvent>.HandleAsync)
        )!;

        foreach (object? handler in _provider.GetServices(handlerType))
        {
            if (handler is null)
            {
                continue;
            }

            await (Task)handleMethod.Invoke(handler, [domainEvent, ct])!;
        }
    }
}
