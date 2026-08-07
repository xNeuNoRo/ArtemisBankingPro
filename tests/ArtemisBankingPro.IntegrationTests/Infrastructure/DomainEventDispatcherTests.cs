using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Domain.Merchants.Events;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class FirstRecordingHandler : IEventHandler<MerchantUserAssociatedEvent> {
    public static readonly List<MerchantUserAssociatedEvent> Received = [];

    public Task HandleAsync(MerchantUserAssociatedEvent domainEvent, CancellationToken ct = default) {
        Received.Add(domainEvent);
        return Task.CompletedTask;
    }
}

public sealed class SecondRecordingHandler : IEventHandler<MerchantUserAssociatedEvent> {
    public static readonly List<MerchantUserAssociatedEvent> Received = [];

    public Task HandleAsync(MerchantUserAssociatedEvent domainEvent, CancellationToken ct = default) {
        Received.Add(domainEvent);
        return Task.CompletedTask;
    }
}

[Collection("SqlServer")]
public sealed class DomainEventDispatcherTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    [Fact]
    public async Task DispatchAsync_CallsEveryRegisteredHandlerWithConcreteEvent() {
        FirstRecordingHandler.Received.Clear();
        SecondRecordingHandler.Received.Clear();

        await using var provider = BuildProvider(services => {
            services.AddScoped<IEventHandler<MerchantUserAssociatedEvent>, FirstRecordingHandler>();
            services.AddScoped<IEventHandler<MerchantUserAssociatedEvent>, SecondRecordingHandler>();
        });

        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();
        var domainEvent = new MerchantUserAssociatedEvent(99, "user-9");

        await dispatcher.DispatchAsync(domainEvent);

        FirstRecordingHandler.Received.Should().ContainSingle(item => item.MerchantId == 99);
        SecondRecordingHandler.Received.Should().ContainSingle(item => item.MerchantId == 99);
    }

    [Fact]
    public async Task DispatchAsync_WithoutHandlers_DoesNotThrow() {
        await using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();

        Func<Task> act = () => dispatcher.DispatchAsync(new MerchantUserAssociatedEvent(1, "user-1"));
        await act.Should().NotThrowAsync();
    }
}
