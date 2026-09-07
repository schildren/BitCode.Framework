using BitCode.Framework.Shared.Application.Eventing;
using Confluent.Kafka;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Implementación concreta de <see cref="IEventPublisher"/> (F3-01) sobre Kafka (F3-02, ADR
/// <c>docs/adr/0005-mensajeria-kafka.md</c>, <c>Accepted</c>).
/// </summary>
/// <remarks>
/// Recibe un <see cref="IProducer{TKey,TValue}"/> ya construido (ver
/// <see cref="KafkaServiceCollectionExtensions.AddSharedMessagingKafka"/>) en vez de crearlo: el
/// productor de Kafka es thread-safe y costoso de crear/destruir (mantiene conexiones y buffers
/// internos), por lo que se comparte una única instancia por proceso — <c>KafkaEventPublisher</c> no
/// es dueño de su ciclo de vida ni lo dispone.
///
/// <c>Key</c> del mensaje: por ahora <see cref="IIntegrationEvent.EventId"/> (valor determinístico por
/// evento, mejor que ninguna key para al menos no perder por completo la posibilidad de correlacionar
/// reintentos), pero la estrategia definitiva de partición (AggregateId/TenantId según orden requerido)
/// es F3-05 — no se resuelve en esta tarea.
/// </remarks>
public sealed class KafkaEventPublisher : IEventPublisher
{
    private readonly IProducer<string, byte[]> _producer;
    private readonly IKafkaTopicNameResolver _topicNameResolver;

    public KafkaEventPublisher(IProducer<string, byte[]> producer, IKafkaTopicNameResolver? topicNameResolver = null)
    {
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _topicNameResolver = topicNameResolver ?? DefaultKafkaTopicNameResolver.Instance;
    }

    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => PublishOneAsync(integrationEvent, cancellationToken);

    public async Task PublishAsync(IEnumerable<IIntegrationEvent> integrationEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvents);

        // Sin atomicidad entre eventos del lote frente al broker (documentado en la firma del
        // contrato, IEventPublisher.PublishAsync(IEnumerable<...>)): cada evento se publica de forma
        // independiente y el llamador (relay de Outbox, F3-03) decide cómo reintentar los que fallen.
        foreach (var integrationEvent in integrationEvents)
        {
            await PublishOneAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishOneAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var topic = _topicNameResolver.ResolveTopicName(integrationEvent.EventType);
        var (value, headers) = KafkaIntegrationEventSerializer.Serialize(integrationEvent);

        var message = new Message<string, byte[]>
        {
            Key = integrationEvent.EventId.ToString(),
            Value = value,
            Headers = headers,
        };

        await _producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);
    }
}
