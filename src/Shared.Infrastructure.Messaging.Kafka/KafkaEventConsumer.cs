using System.Text;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Application.Inbox;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Consumidor base de Kafka (F3-02) para un <see cref="IIntegrationEvent"/> concreto: se suscribe al
/// tópico correspondiente, deserializa cada mensaje y lo entrega, deduplicado, a un
/// <see cref="IEventConsumer{TEvent}"/> (F3-04: coordinación real con Inbox, F1-24).
/// </summary>
/// <remarks>
/// Composición completa por mensaje entregado por <see cref="ConsumeAndHandleOnceAsync"/> (F3-04):
/// Kafka entrega el mensaje → se deserializa a <typeparamref name="TEvent"/>
/// (<see cref="KafkaIntegrationEventSerializer.Deserialize{TEvent}"/>) → se abre un scope de DI nuevo
/// (<see cref="IServiceScopeFactory.CreateAsyncScope"/>) → se resuelve
/// <see cref="IInboxMessageProcessor"/> y <see cref="IEventConsumer{TEvent}"/> DE ESE MISMO scope → se
/// invoca <c>IInboxMessageProcessor.ProcessAsync(EventId, ...)</c> pasando
/// <c>IEventConsumer{TEvent}.ConsumeAsync</c> como <c>handler</c> → si <c>ProcessAsync</c> retorna sin
/// lanzar (ya sea porque ejecutó el handler con éxito y lo persistió junto con la fila de Inbox en un
/// único <c>SaveChangesAsync</c>, ya sea porque detectó un duplicado ya procesado y lo descartó sin
/// ejecutar nada) recién ENTONCES se confirma el offset de Kafka (<c>Commit</c>). Si
/// <c>ProcessAsync</c> lanza (el handler de negocio falló), el offset NO se confirma y el scope se
/// descarta: el mismo mensaje se reintenta en la siguiente llamada, sin ninguna fila de Inbox
/// sobreviviendo (ver remarks de <c>InboxMessageProcessor</c>) — no se lo trata como duplicado.
///
/// <see cref="IIntegrationEvent.EventId"/> es el <c>messageId</c> que identifica el mensaje ante el
/// Inbox: es la clave natural de deduplicación documentada por el propio contrato
/// (<see cref="IIntegrationEvent"/>), estable entre reentregas del mismo mensaje por Kafka (rebalance,
/// reinicio del consumidor, o el duplicado aceptable que puede introducir el relay de Outbox, F3-03,
/// si el proceso muere entre publicar y marcar la fila) — nunca el offset/partición de Kafka, que no
/// identifica la instancia lógica del evento sino su posición física en el tópico.
///
/// Por qué un scope de DI por mensaje (y no un <see cref="IEventConsumer{TEvent}"/> ya construido, como
/// hacía F3-02): tanto <c>IInboxMessageProcessor</c> como cualquier <c>IEventConsumer{TEvent}</c> real
/// necesitan el mismo <c>DbContext</c>/<c>IUnitOfWork</c> de scope para que la fila de Inbox y el efecto
/// de negocio se persistan en el mismo <c>SaveChangesAsync</c> atómico (ver remarks de
/// <c>InboxMessageProcessor</c>) — un <c>DbContext</c> es <c>Scoped</c> por diseño de EF Core, nunca
/// puede compartirse de forma segura entre mensajes consumidos secuencialmente por la misma instancia
/// de este consumidor (ni, sobre todo, entre llamadas concurrentes). Mismo motivo por el que
/// <c>OutboxPublisherBackgroundService</c> (F3-03) crea un scope nuevo por ciclo en vez de inyectar
/// <c>OutboxBatchProcessor</c> directamente en su constructor — la contraparte de "scope por unidad de
/// trabajo" del lado consumidor. El bounded context que usa este consumidor debe registrar su propio
/// <c>IEventConsumer{TEvent}</c> concreto como <c>Scoped</c> en el mismo contenedor de DI del que sale
/// <paramref name="scopeFactory"/> (típicamente el <c>IServiceProvider</c> raíz de la aplicación,
/// ya sea vía un <c>BackgroundService</c> propio o cualquier host que aloje este consumidor) — este
/// proyecto (Shared.Infrastructure.Messaging.Kafka) no impone ningún host concreto, a diferencia del
/// relay de Outbox, que sí trae su propio <c>OutboxPublisherBackgroundService</c>: cada evento de
/// integración concreto necesita su propio consumidor/tópico/grupo, por lo que la decisión de "cómo y
/// cuándo correr el loop de consumo" queda en el proyecto consumidor (ver
/// <c>docs/guia-inbox-consumer.md</c> para un ejemplo completo).
///
/// Límite conocido (documentado también en <c>docs/guia-inbox-consumer.md</c>): esta clase NO reintenta
/// automáticamente ni clasifica errores transitorios/permanentes (F3-07) — si <c>ProcessAsync</c> lanza,
/// la excepción se propaga tal cual a quien llamó <see cref="ConsumeAndHandleOnceAsync"/>, que decide
/// cómo reaccionar (por ejemplo, un loop que loguea y continúa con el próximo ciclo de sondeo, igual que
/// <c>OutboxPublisherBackgroundService</c> hace con un fallo de ciclo completo).
/// </remarks>
/// <typeparam name="TEvent">Tipo concreto del evento de integración entregado por este consumidor.</typeparam>
public sealed class KafkaEventConsumer<TEvent> : IDisposable
    where TEvent : class, IIntegrationEvent
{
    private readonly IConsumer<string, byte[]> _consumer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _eventType;

    /// <param name="options">Configuración de conexión/autenticación al broker.</param>
    /// <param name="eventType">
    /// <see cref="IIntegrationEvent.EventType"/> del evento que maneja este consumidor — usado para
    /// resolver el tópico al que suscribirse (F3-05 define la estrategia definitiva de tópicos/partición)
    /// y como <c>messageType</c> de trazabilidad ante <see cref="IInboxMessageProcessor.ProcessAsync"/>.
    /// </param>
    /// <param name="scopeFactory">
    /// Fábrica de scopes de DI (F3-04): se crea un scope nuevo por mensaje consumido, del que se
    /// resuelven <see cref="IInboxMessageProcessor"/> y <see cref="IEventConsumer{TEvent}"/> — ambos
    /// deben estar registrados (típicamente <c>Scoped</c>) en el contenedor raíz al que pertenece esta
    /// fábrica. Ver remarks de la clase.
    /// </param>
    /// <param name="consumerGroupIdOverride">Group id explícito; si no se indica, usa <see cref="KafkaMessagingOptions.ConsumerGroupId"/>.</param>
    /// <param name="topicNameResolver">Resolución de tópico; por defecto <see cref="DefaultKafkaTopicNameResolver"/>.</param>
    public KafkaEventConsumer(
        KafkaMessagingOptions options,
        string eventType,
        IServiceScopeFactory scopeFactory,
        string? consumerGroupIdOverride = null,
        IKafkaTopicNameResolver? topicNameResolver = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _eventType = eventType;

        var resolver = topicNameResolver ?? DefaultKafkaTopicNameResolver.Instance;
        var topic = resolver.ResolveTopicName(eventType);

        _consumer = new ConsumerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildConsumerConfig(options, consumerGroupIdOverride))
            .Build();
        _consumer.Subscribe(topic);
    }

    /// <summary>
    /// Espera hasta <paramref name="timeout"/> por un único mensaje del tópico suscripto; si llega, lo
    /// deserializa y lo procesa de forma deduplicada vía <see cref="IInboxMessageProcessor.ProcessAsync"/>
    /// (F3-04) — solo si retorna sin excepción (procesado o descartado por duplicado) se confirma el
    /// offset. Devuelve <see langword="false"/> si no llegó ningún mensaje dentro del timeout (no es un
    /// error: el tópico puede estar vacío).
    /// </summary>
    public async Task<bool> ConsumeAndHandleOnceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var result = await Task.Run(() => _consumer.Consume(timeout), cancellationToken).ConfigureAwait(false);
        if (result is null || result.IsPartitionEOF || result.Message is null)
        {
            return false;
        }

        var integrationEvent = KafkaIntegrationEventSerializer.Deserialize<TEvent>(result.Message.Value);

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var inboxProcessor = scope.ServiceProvider.GetRequiredService<IInboxMessageProcessor>();
            var handler = scope.ServiceProvider.GetRequiredService<IEventConsumer<TEvent>>();

            await inboxProcessor.ProcessAsync(
                integrationEvent.EventId.ToString(),
                _eventType,
                Encoding.UTF8.GetString(result.Message.Value),
                ct => handler.ConsumeAsync(integrationEvent, ct),
                cancellationToken).ConfigureAwait(false);
        }

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
