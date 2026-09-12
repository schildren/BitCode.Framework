using System.Collections.Concurrent;
using BitCode.Framework.Shared.Application.Eventing;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests;

/// <summary>Test double de <see cref="IEventConsumer{TEvent}"/> que registra cada evento recibido, para verificar el round-trip productor→consumidor contra un broker real.</summary>
public sealed class RecordingEventConsumer<TEvent> : IEventConsumer<TEvent>
    where TEvent : IIntegrationEvent
{
    public ConcurrentQueue<TEvent> ReceivedEvents { get; } = new();

    public Task ConsumeAsync(TEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ReceivedEvents.Enqueue(integrationEvent);
        return Task.CompletedTask;
    }
}
