using BitCode.Framework.Shared.Application.Eventing;
using Confluent.Kafka;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Consumidor base de Kafka (F3-02) para un <see cref="IIntegrationEvent"/> concreto: se suscribe al
/// tópico correspondiente, deserializa cada mensaje y lo entrega a un <see cref="IEventConsumer{TEvent}"/>.
/// </summary>
/// <remarks>
/// Deliberadamente NO coordina con Inbox (F1-24) ni con deduplicación (F3-04 es la tarea que integra
/// este consumidor con <c>IInboxMessageProcessor.ProcessAsync</c>): esta clase solo resuelve, en
/// aislamiento, "puedo suscribirme a un tópico Kafka real, deserializar el mensaje y pasárselo a un
/// handler de negocio" (criterio de aceptación de F3-02, "pruebas con broker real").
///
/// Confirmación de offset: <see cref="ConsumeAndHandleOnceAsync"/> desactiva el auto-commit
/// (<c>EnableAutoCommit = false</c>, ver <see cref="KafkaClientConfigFactory.BuildConsumerConfig"/>) y
/// solo confirma el offset DESPUÉS de que <see cref="IEventConsumer{TEvent}.ConsumeAsync"/> termina sin
/// excepción — esto es lo más cerca que puede llegar esta tarea, sin todavía Inbox real, de la semántica
/// exigida por la Fase 3 ("Confirmación: únicamente después de persistir el efecto o Inbox"): si el
/// proceso se cae entre el commit del efecto de negocio y este <c>Commit</c>, Kafka reentrega el mismo
/// mensaje al reiniciar (at-least-once, nunca se pierde), y será F3-04 quien deba además descartarlo si
/// ya fue procesado (idempotencia real vía Inbox). Si <c>ConsumeAsync</c> lanza, el offset NO se
/// confirma: el mismo mensaje se reintenta en la siguiente llamada.
/// </remarks>
/// <typeparam name="TEvent">Tipo concreto del evento de integración entregado por este consumidor.</typeparam>
public sealed class KafkaEventConsumer<TEvent> : IDisposable
    where TEvent : class, IIntegrationEvent
{
    private readonly IConsumer<string, byte[]> _consumer;
    private readonly IEventConsumer<TEvent> _handler;

    /// <param name="options">Configuración de conexión/autenticación al broker.</param>
    /// <param name="eventType">
    /// <see cref="IIntegrationEvent.EventType"/> del evento que maneja este consumidor — usado para
    /// resolver el tópico al que suscribirse (F3-05 define la estrategia definitiva de tópicos/partición).
    /// </param>
    /// <param name="handler">Efecto de negocio a ejecutar por cada mensaje entregado (F3-01).</param>
    /// <param name="consumerGroupIdOverride">Group id explícito; si no se indica, usa <see cref="KafkaMessagingOptions.ConsumerGroupId"/>.</param>
    /// <param name="topicNameResolver">Resolución de tópico; por defecto <see cref="DefaultKafkaTopicNameResolver"/>.</param>
    public KafkaEventConsumer(
        KafkaMessagingOptions options,
        string eventType,
        IEventConsumer<TEvent> handler,
        string? consumerGroupIdOverride = null,
        IKafkaTopicNameResolver? topicNameResolver = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));

        var resolver = topicNameResolver ?? DefaultKafkaTopicNameResolver.Instance;
        var topic = resolver.ResolveTopicName(eventType);

        _consumer = new ConsumerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildConsumerConfig(options, consumerGroupIdOverride))
            .Build();
        _consumer.Subscribe(topic);
    }

    /// <summary>
    /// Espera hasta <paramref name="timeout"/> por un único mensaje del tópico suscripto; si llega,
    /// lo deserializa, invoca <see cref="IEventConsumer{TEvent}.ConsumeAsync"/> y solo si termina sin
    /// excepción confirma el offset. Devuelve <see langword="false"/> si no llegó ningún mensaje dentro
    /// del timeout (no es un error: el tópico puede estar vacío).
    /// </summary>
    public async Task<bool> ConsumeAndHandleOnceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var result = await Task.Run(() => _consumer.Consume(timeout), cancellationToken).ConfigureAwait(false);
        if (result is null || result.IsPartitionEOF || result.Message is null)
        {
            return false;
        }

        var integrationEvent = KafkaIntegrationEventSerializer.Deserialize<TEvent>(result.Message.Value);
        await _handler.ConsumeAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        _consumer.Commit(result);

        return true;
    }

    public void Dispose()
    {
        // Close() abandona el grupo de consumidores de forma prolija (trigger inmediato de rebalance
        // en vez de esperar el session timeout) antes de liberar el handle nativo.
        _consumer.Close();
        _consumer.Dispose();
    }
}
