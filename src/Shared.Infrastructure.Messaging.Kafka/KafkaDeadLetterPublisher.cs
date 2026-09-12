using System.Text;
using BitCode.Framework.Shared.Application.Eventing;
using Confluent.Kafka;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Implementación Kafka de <see cref="IDeadLetterPublisher"/> (F3-08): publica una copia de un mensaje
/// agotado (F3-07) a un tópico dead-letter real, con metadatos enriquecidos en headers para que un
/// operador que solo inspecciona Kafka (sin necesitar la base de datos del relay de Outbox) pueda
/// filtrar/decidir sin deserializar el payload completo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Naming del tópico dead-letter:</b> <c>{tópico original}.dlq</c> — el mismo
/// <see cref="IKafkaTopicNameResolver"/> que ya resuelve el tópico "feliz" de un
/// <see cref="DeadLetterEnvelope.EventType"/> (F3-02/F3-05), con el sufijo <c>.dlq</c> agregado. Se eligió
/// un sufijo fijo por tópico original (en vez de un único tópico DLQ global) para que la retención,
/// permisos (F3-11) y herramientas de reprocesamiento puedan operar por tipo de evento sin tener que
/// filtrar un tópico compartido por todos los bounded contexts — mismo criterio de aislamiento que ya
/// usa el esquema de tópicos "feliz" (un tópico por <c>EventType</c>).
/// </para>
/// <para>
/// <b>Dónde se invoca (decisión de diseño):</b> esta clase NO decide cuándo un mensaje está agotado — solo
/// sabe publicar un <see cref="DeadLetterEnvelope"/> ya armado. La decisión de "cuándo" queda en cada lado
/// que ya tiene esa señal: <c>OutboxBatchProcessor</c> (<c>Shared.Infrastructure.Persistence</c>) la invoca
/// como un paso adicional, best-effort, inmediatamente después de persistir
/// <c>OutboxMessage.ExhaustedAtUtc</c> — nunca ANTES ni en vez de esa escritura, para que la fila de
/// <c>OutboxMessage</c> (la fuente de verdad que nunca se pierde, F3-03) quede persistida incluso si la
/// publicación a DLQ falla. <c>KafkaEventConsumer{TEvent}</c> la invoca al capturar
/// <see cref="EventProcessingExhaustedException"/>. Se evaluó envolver esta lógica directamente dentro del
/// relay/consumidor (sin una interfaz separada) pero eso acoplaría ambos componentes a un paquete de broker
/// concreto — mismo motivo por el que <see cref="IEventPublisher"/> (F3-01) ya es una interfaz separada de
/// <c>KafkaEventPublisher</c>.
/// </para>
/// </remarks>
public sealed class KafkaDeadLetterPublisher : IDeadLetterPublisher
{
    /// <summary>Sufijo agregado al tópico original resuelto por <see cref="IKafkaTopicNameResolver"/> para obtener el tópico dead-letter.</summary>
    public const string DeadLetterTopicSuffix = ".dlq";

    public const string OriginalEventTypeHeader = "bitcode-dlq-original-event-type";
    public const string OriginalTopicHeader = "bitcode-dlq-original-topic";
    public const string ReasonHeader = "bitcode-dlq-reason";
    public const string AttemptsHeader = "bitcode-dlq-attempts";
    public const string ExhaustedAtUtcHeader = "bitcode-dlq-exhausted-at-utc";
    public const string SourceMessageIdHeader = "bitcode-dlq-source-message-id";

    private readonly IProducer<string, byte[]> _producer;
    private readonly IKafkaTopicNameResolver _topicNameResolver;

    public KafkaDeadLetterPublisher(IProducer<string, byte[]> producer, IKafkaTopicNameResolver? topicNameResolver = null)
    {
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _topicNameResolver = topicNameResolver ?? DefaultKafkaTopicNameResolver.Instance;
    }

    public async Task PublishAsync(DeadLetterEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var originalTopic = _topicNameResolver.ResolveTopicName(envelope.EventType);
        var deadLetterTopic = originalTopic + DeadLetterTopicSuffix;

        var headers = new Headers
        {
            { OriginalEventTypeHeader, Encoding.UTF8.GetBytes(envelope.EventType) },
            { OriginalTopicHeader, Encoding.UTF8.GetBytes(originalTopic) },
            { ReasonHeader, Encoding.UTF8.GetBytes(envelope.Reason) },
            { AttemptsHeader, Encoding.UTF8.GetBytes(envelope.Attempts.ToString()) },
            { ExhaustedAtUtcHeader, Encoding.UTF8.GetBytes(envelope.ExhaustedAtUtc.ToString("O")) },
        };

        if (envelope.SourceMessageId is not null)
        {
            headers.Add(SourceMessageIdHeader, Encoding.UTF8.GetBytes(envelope.SourceMessageId));
        }

        var message = new Message<string, byte[]>
        {
            // Clave: el SourceMessageId (si existe) mantiene el mismo mensaje agrupable ante reprocesos
            // repetidos; sin él, un Guid nuevo (ningún requisito de orden entre mensajes DLQ distintos).
            Key = envelope.SourceMessageId ?? Guid.NewGuid().ToString(),
            Value = envelope.Payload,
            Headers = headers,
        };

        await _producer.ProduceAsync(deadLetterTopic, message, cancellationToken).ConfigureAwait(false);
    }
}
