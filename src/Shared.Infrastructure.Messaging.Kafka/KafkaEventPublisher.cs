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
/// <c>Key</c> del mensaje (F3-05, <c>IHasPartitionKey</c>): si <see cref="IIntegrationEvent"/> implementa
/// <see cref="IHasPartitionKey"/>, se usa <see cref="IHasPartitionKey.PartitionKey"/> como <c>Key</c> —
/// dos eventos con la misma <c>PartitionKey</c> quedan en la misma partición del tópico, así que un
/// consumidor de esa partición los recibe en el mismo orden en que se publicaron (ver
/// <c>docs/guia-eventing-contratos.md</c>, sección "Particionamiento (F3-05)"). Si el evento NO
/// implementa <see cref="IHasPartitionKey"/>, se usa <see cref="IIntegrationEvent.EventId"/> como antes
/// de F3-05 (placeholder razonable, sin ninguna garantía de orden entre eventos relacionados).
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
            // F3-05: partición por PartitionKey explícito cuando el evento lo declara; si no,
            // fallback a EventId (comportamiento heredado de F3-02, sin garantía de orden).
            Key = integrationEvent is IHasPartitionKey withPartitionKey
                ? withPartitionKey.PartitionKey
                : integrationEvent.EventId.ToString(),
            Value = value,
            Headers = headers,
        };

        await _producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);
    }
}
