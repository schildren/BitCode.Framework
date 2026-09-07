using System.Collections.Concurrent;
using System.Text;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Application.Inbox;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
/// <b>Clasificación y backoff (F3-07), y su límite real de diseño:</b> si <c>ProcessAsync</c> lanza (el
/// handler de negocio falló), esta clase clasifica la excepción con <see cref="IEventPublishFailureClassifier"/>
/// y, si todavía queda margen (<see cref="EventRetryPolicyOptions.MaxAttempts"/> de <see cref="RetryOptions"/>),
/// espera el backoff calculado (<see cref="EventRetryBackoff.CalculateDelay"/>) ANTES de volver a lanzar
/// la excepción original — a diferencia del relay de Outbox (F3-03/F3-07), que puede programar el
/// próximo intento como una marca de tiempo futura sin bloquear nada (persistida en
/// <c>OutboxMessage.LockedUntilUtc</c>, consultada recién en el siguiente ciclo de sondeo),
/// <see cref="ConsumeAndHandleOnceAsync"/> no tiene ningún lugar donde "programar" un reintento futuro
/// sin bloquear: no hay loop propio (lo maneja el host que la invoca), y Kafka vuelve a entregar el
/// mismo mensaje en la siguiente llamada sin que este consumidor pueda decirle "esperá". La única forma
/// de introducir backoff real es demorar la propia llamada que falló con <c>Task.Delay</c> antes de
/// devolver el control — lo que retrasa también el procesamiento de cualquier mensaje siguiente que este
/// consumidor recibiría después (una limitación real, no cosmética, de "sin loop propio"; documentada
/// también en <c>docs/guia-inbox-consumer.md</c>).
///
/// El conteo de intentos por mensaje es EN MEMORIA (<see cref="_attemptsByMessageId"/>, por instancia de
/// este consumidor) — no persistido: un reinicio del proceso pierde el conteo y el mensaje vuelve a
/// tener margen completo de reintentos. Esto es deliberado (Inbox, F1-24, solo persiste mensajes que
/// terminaron con éxito, nunca intentos fallidos — agregar esa persistencia es un cambio de esquema de
/// Inbox fuera del alcance mínimo de F3-07) y es la razón por la que, del lado consumidor, el límite
/// máximo de reintentos es una protección "mejor esfuerzo" contra un mensaje que falla en loop rápido
/// dentro de la MISMA vida del proceso, no una garantía dura de "nunca más de N intentos totales" como sí
/// lo es del lado del relay de Outbox (persistido en <c>OutboxMessage.RetryCount</c>/<c>ExhaustedAtUtc</c>).
/// Al agotar el límite, se lanza <see cref="EventProcessingExhaustedException"/> (envolviendo la
/// excepción original) en vez de la excepción original tal cual — el punto de extensión explícito que
/// F3-08 (DLQ) necesita para decidir enrutar el mensaje a una cola de mensajes muertos en vez de seguir
/// reintentando; F3-07 no implementa ese enrutamiento, solo lo señaliza con un tipo de excepción
/// distinguible.
///
/// <b>Poison messages (F3-09) vs. agotamiento de reintentos del handler (F3-07): son dos fallos
/// distintos con dos tratamientos distintos.</b> Un mensaje "poison" es uno que
/// <see cref="KafkaIntegrationEventSerializer.Deserialize{TEvent}"/> ni siquiera puede convertir a
/// <typeparamref name="TEvent"/> (JSON corrupto, forma incompatible con el tipo) — un error del MENSAJE,
/// no del handler de negocio, y por definición PERMANENTE: reintentarlo nunca lo va a arreglar, porque
/// nunca llega a ejecutarse ningún handler. <see cref="ConsumeAndHandleOnceAsync"/> detecta este caso
/// ANTES de abrir el scope de DI / invocar Inbox, y lo aísla directo a DLQ (ver
/// <c>IsolatePoisonMessageAsync</c>) sin pasar nunca por <see cref="HandleProcessingFailureAsync"/>, sin
/// consultar <see cref="IEventPublishFailureClassifier"/> (que clasifica excepciones del HANDLER, no de
/// deserialización) y sin acumular intentos en <see cref="_attemptsByMessageId"/> — un único intento
/// alcanza para clasificarlo. El agotamiento de reintentos (F3-07/<see cref="EventProcessingExhaustedException"/>),
/// en cambio, es un mensaje que SÍ se pudo deserializar e interpretar, pero cuyo handler de negocio falla
/// persistentemente (dependencia caída, bug del handler, dato de negocio inválido para esa lógica, etc.)
/// — ahí sí corresponde backoff con reintentos porque el error puede ser transitorio, y solo se aísla a
/// DLQ después de agotar <see cref="EventRetryPolicyOptions.MaxAttempts"/> o de clasificarse como
/// <see cref="EventPublishFailureKind.Permanent"/>. Ambos casos terminan aislados en el mismo tópico
/// dead-letter (mismo <see cref="IDeadLetterPublisher"/>) con el offset confirmado igual, pero el motivo
/// (<see cref="DeadLetterEnvelope.Reason"/>) distingue "PoisonMessage" de un mensaje de negocio agotado —
/// ver <c>docs/guia-inbox-consumer.md</c> y <c>docs/runbook-dlq.md</c> para el detalle operativo.
/// </remarks>
/// <typeparam name="TEvent">Tipo concreto del evento de integración entregado por este consumidor.</typeparam>
public sealed class KafkaEventConsumer<TEvent> : IDisposable
    where TEvent : class, IIntegrationEvent
{
    private readonly IConsumer<string, byte[]> _consumer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _eventType;
    private readonly IEventPublishFailureClassifier _failureClassifier;
    private readonly IDeadLetterPublisher? _deadLetterPublisher;
    private readonly ILogger<KafkaEventConsumer<TEvent>>? _logger;
    private readonly ConcurrentDictionary<string, int> _attemptsByMessageId = new();

    /// <summary>
    /// Política de reintentos con backoff (F3-07) aplicada a mensajes cuyo handler falla — ver el
    /// <c>remarks</c> de la clase para la limitación real de diseño de este lado (backoff bloqueante,
    /// conteo en memoria no persistido).
    /// </summary>
    public EventRetryPolicyOptions RetryOptions { get; }

    /// <param name="options">Configuración de conexión/autenticación al broker.</param>
    /// <param name="eventType">
    /// <see cref="IIntegrationEvent.EventType"/> del evento que maneja este consumidor — usado para
    /// resolver el tópico al que suscribirse y como <c>messageType</c> de trazabilidad ante
    /// <see cref="IInboxMessageProcessor.ProcessAsync"/>. La clave de partición dentro de ese tópico
    /// (AggregateId/TenantId según orden requerido) la define el publicador (F3-05,
    /// <c>IHasPartitionKey</c>/<c>KafkaEventPublisher</c>) — este consumidor solo se suscribe al tópico
    /// completo (todas sus particiones) y no participa de esa decisión.
    /// </param>
    /// <param name="scopeFactory">
    /// Fábrica de scopes de DI (F3-04): se crea un scope nuevo por mensaje consumido, del que se
    /// resuelven <see cref="IInboxMessageProcessor"/> y <see cref="IEventConsumer{TEvent}"/> — ambos
    /// deben estar registrados (típicamente <c>Scoped</c>) en el contenedor raíz al que pertenece esta
    /// fábrica. Ver remarks de la clase.
    /// </param>
    /// <param name="consumerGroupIdOverride">Group id explícito; si no se indica, usa <see cref="KafkaMessagingOptions.ConsumerGroupId"/>.</param>
    /// <param name="topicNameResolver">Resolución de tópico; por defecto <see cref="DefaultKafkaTopicNameResolver"/>.</param>
    /// <param name="retryOptions">
    /// Política de reintentos (F3-07); por defecto una nueva <see cref="EventRetryPolicyOptions"/> con
    /// los valores por defecto documentados en esa clase.
    /// </param>
    /// <param name="failureClassifier">
    /// Clasificador de excepciones del handler de negocio (F3-07); por defecto
    /// <see cref="DefaultEventPublishFailureClassifier"/> (trata todo como transitorio, el valor por
    /// defecto más seguro para excepciones de negocio arbitrarias sin información específica de Kafka).
    /// </param>
    public KafkaEventConsumer(
        KafkaMessagingOptions options,
        string eventType,
        IServiceScopeFactory scopeFactory,
        string? consumerGroupIdOverride = null,
        IKafkaTopicNameResolver? topicNameResolver = null,
        EventRetryPolicyOptions? retryOptions = null,
        IEventPublishFailureClassifier? failureClassifier = null,
        IDeadLetterPublisher? deadLetterPublisher = null,
        ILogger<KafkaEventConsumer<TEvent>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _eventType = eventType;
        RetryOptions = retryOptions ?? new EventRetryPolicyOptions();
        _failureClassifier = failureClassifier ?? new DefaultEventPublishFailureClassifier();
        _deadLetterPublisher = deadLetterPublisher;
        _logger = logger;

        var resolver = topicNameResolver ?? DefaultKafkaTopicNameResolver.Instance;
        var topic = resolver.ResolveTopicName(eventType);

        _consumer = new ConsumerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildConsumerConfig(options, consumerGroupIdOverride))
            .Build();
        _consumer.Subscribe(topic);
    }

    /// <summary>
    /// Espera hasta <paramref name="timeout"/> por un único mensaje del tópico suscripto; si llega,
    /// intenta deserializarlo y, si lo logra, lo procesa de forma deduplicada vía
    /// <see cref="IInboxMessageProcessor.ProcessAsync"/> (F3-04) — solo si retorna sin excepción
    /// (procesado o descartado por duplicado) se confirma el offset. Devuelve <see langword="false"/> si
    /// no llegó ningún mensaje dentro del timeout (no es un error: el tópico puede estar vacío).
    /// </summary>
    /// <remarks>
    /// F3-09: si el mensaje NI SIQUIERA se puede deserializar (poison message), se aísla directo a DLQ
    /// (<c>IsolatePoisonMessageAsync</c>) sin pasar por ningún reintento y el offset se confirma siempre
    /// (retorna <see langword="true"/>) — ver el <c>remarks</c> de la clase para la distinción completa
    /// con el agotamiento de reintentos de F3-07.
    ///
    /// F3-07: si el mensaje SÍ se deserializó pero el handler de negocio falla, clasifica la excepción y
    /// decide entre (a) esperar el backoff calculado y volver a lanzar la excepción original (todavía
    /// queda margen de reintentos: el offset no se confirma, Kafka reentrega el mismo mensaje en la
    /// próxima llamada) o (b) lanzar <see cref="EventProcessingExhaustedException"/> (se agotó el margen,
    /// o el error es <see cref="EventPublishFailureKind.Permanent"/>) — ver el <c>remarks</c> de la clase
    /// para la limitación real de diseño de este lado (backoff bloqueante, conteo en memoria no
    /// persistido).
    /// </remarks>
    public async Task<bool> ConsumeAndHandleOnceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var result = await Task.Run(() => _consumer.Consume(timeout), cancellationToken).ConfigureAwait(false);
        if (result is null || result.IsPartitionEOF || result.Message is null)
        {
            return false;
        }

        TEvent integrationEvent;
        try
        {
            integrationEvent = KafkaIntegrationEventSerializer.Deserialize<TEvent>(result.Message.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // F3-09: un mensaje que ni siquiera se puede deserializar es un error PERMANENTE del mensaje
            // en sí (JSON corrupto, forma incompatible con TEvent, etc.) — nunca del handler de negocio
            // (F3-07), así que jamás pasa por HandleProcessingFailureAsync/IEventPublishFailureClassifier
            // (que clasifican fallos del HANDLER, no de deserialización) ni acumula intentos en
            // _attemptsByMessageId: reintentarlo no lo va a arreglar nunca, se aísla directo a DLQ (ver
            // IsolatePoisonMessageAsync) y el offset se confirma siempre para que la partición no quede
            // bloqueada.
            await IsolatePoisonMessageAsync(result, ex, cancellationToken).ConfigureAwait(false);
            _consumer.Commit(result);
            return true;
        }

        var messageId = integrationEvent.EventId.ToString();

        try
        {
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var inboxProcessor = scope.ServiceProvider.GetRequiredService<IInboxMessageProcessor>();
                var handler = scope.ServiceProvider.GetRequiredService<IEventConsumer<TEvent>>();

                await inboxProcessor.ProcessAsync(
                    messageId,
                    _eventType,
                    Encoding.UTF8.GetString(result.Message.Value),
                    ct => handler.ConsumeAsync(integrationEvent, ct),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // HandleProcessingFailureAsync incrementa el conteo de intentos y, si ya no queda margen,
            // lanza EventProcessingExhaustedException. Si todavía queda margen, espera el backoff
            // calculado y retorna sin lanzar, así que el "throw;" final relanza la excepción ORIGINAL.
            try
            {
                await HandleProcessingFailureAsync(messageId, ex, cancellationToken).ConfigureAwait(false);
            }
            catch (EventProcessingExhaustedException exhausted)
            {
                // F3-08 (DLQ): se agotó el margen — a diferencia de F3-07 (donde el offset nunca se
                // confirmaba y Kafka reentregaba el mismo mensaje sin fin, bloqueando la partición), acá
                // se publica una copia best-effort al tópico dead-letter y se confirma el offset: el
                // mensaje queda "en cuarentena" en vez de seguir bloqueando la entrega de los mensajes
                // siguientes de la misma partición (el caso puntual de aislamiento de poison messages que
                // resuelve F3-08 con el mecanismo que ya tiene disponible; el aislamiento general —
                // cualquier mensaje inválido, no solo el que agotó reintentos— es F3-09).
                await PublishToDeadLetterAsync(exhausted, result.Message.Value, cancellationToken).ConfigureAwait(false);
                _consumer.Commit(result);
                throw;
            }

            throw; // El mensaje todavía tiene margen: se relanza para que el offset no se confirme.
        }

        // Éxito: ya no hace falta seguir contando intentos fallidos previos de este mensaje.
        _attemptsByMessageId.TryRemove(messageId, out _);
        _consumer.Commit(result);

        return true;
    }

    /// <summary>
    /// Clasifica el fallo, actualiza el conteo de intentos EN MEMORIA de este mensaje y decide entre
    /// esperar el backoff (deja <paramref name="cause"/> para que el llamador la relance tal cual) o
    /// lanzar <see cref="EventProcessingExhaustedException"/> si ya no corresponde reintentar más.
    /// </summary>
    private async Task HandleProcessingFailureAsync(string messageId, Exception cause, CancellationToken cancellationToken)
    {
        var attempts = _attemptsByMessageId.AddOrUpdate(messageId, 1, static (_, existing) => existing + 1);
        var failureKind = _failureClassifier.Classify(cause);
        var isExhausted = failureKind == EventPublishFailureKind.Permanent || EventRetryBackoff.IsExhausted(attempts, RetryOptions);

        if (isExhausted)
        {
            _attemptsByMessageId.TryRemove(messageId, out _);
            throw new EventProcessingExhaustedException(messageId, _eventType, attempts, cause);
        }

        var delay = EventRetryBackoff.CalculateDelay(attempts, RetryOptions);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// F3-08: publica una copia best-effort de <paramref name="payload"/> (los mismos bytes originales del
    /// mensaje Kafka, sin transformar) a un tópico dead-letter, con los metadatos disponibles de
    /// <paramref name="exhausted"/>. Si <see cref="_deadLetterPublisher"/> no está configurado (ningún
    /// adapter de broker lo registró) o la publicación falla, solo se deja un warning en el log — nunca se
    /// relanza desde acá: el llamador ya va a relanzar <paramref name="exhausted"/> de todos modos, y un
    /// fallo de esta notificación de conveniencia no debe impedir seguir consumiendo mensajes siguientes.
    /// </summary>
    private async Task PublishToDeadLetterAsync(EventProcessingExhaustedException exhausted, byte[] payload, CancellationToken cancellationToken)
    {
        if (_deadLetterPublisher is null)
        {
            return;
        }

        try
        {
            await _deadLetterPublisher.PublishAsync(
                new DeadLetterEnvelope
                {
                    EventType = exhausted.EventType,
                    Payload = payload,
                    Reason = exhausted.InnerException?.Message ?? exhausted.Message,
                    Attempts = exhausted.Attempts,
                    ExhaustedAtUtc = DateTime.UtcNow,
                    SourceMessageId = exhausted.MessageId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(
                ex,
                "No se pudo publicar el mensaje {MessageId} (EventType {EventType}) al tópico dead-letter; " +
                "el offset se confirma igual (el mensaje queda agotado del lado consumidor).",
                exhausted.MessageId,
                exhausted.EventType);
        }
    }

    /// <summary>
    /// F3-09: aísla un mensaje "poison" (no se pudo deserializar a <typeparamref name="TEvent"/>) sin
    /// pasar por el ciclo de reintentos de F3-07 — un JSON corrupto o de forma incompatible nunca se
    /// arregla reintentando, así que se publica una copia best-effort al tópico dead-letter (reutilizando
    /// <see cref="_deadLetterPublisher"/>, F3-08) con el motivo <c>"PoisonMessage"</c> y el offset se
    /// confirma siempre desde el llamador, incluso si esta publicación falla.
    /// </summary>
    /// <remarks>
    /// Trade-off deliberado si <see cref="_deadLetterPublisher"/> no está configurado o su publicación
    /// también falla: igual se deja avanzar el offset (solo un warning en el log). La alternativa —no
    /// confirmar y bloquear la partición hasta que el DLQ vuelva a estar disponible— contradice el
    /// criterio de aceptación de esta tarea ("Consumer continúa operando") y dejaría bloqueando
    /// indefinidamente a mensajes siguientes válidos por un mensaje que, además, NUNCA se va a poder
    /// deserializar aunque el DLQ vuelva a funcionar (a diferencia de un backend caído, esto no es
    /// transitorio). El costo aceptado es perder esa copia dead-letter puntual — mismo criterio
    /// "best-effort" que <see cref="PublishToDeadLetterAsync"/> ya aplica en F3-08 para el agotamiento de
    /// reintentos del handler.
    /// </remarks>
    private async Task IsolatePoisonMessageAsync(ConsumeResult<string, byte[]> result, Exception deserializationError, CancellationToken cancellationToken)
    {
        _logger?.LogWarning(
            deserializationError,
            "Mensaje poison en el tópico del evento {EventType} (partición {Partition}, offset {Offset}): no se pudo deserializar a {EventClrType}. Se aísla a DLQ y se confirma el offset para no bloquear la partición.",
            _eventType,
            result.Partition.Value,
            result.Offset.Value,
            typeof(TEvent).FullName);

        if (_deadLetterPublisher is null)
        {
            return;
        }

        try
        {
            await _deadLetterPublisher.PublishAsync(
                new DeadLetterEnvelope
                {
                    EventType = _eventType,
                    Payload = result.Message.Value,
                    Reason = $"PoisonMessage (DeserializationFailure): {deserializationError.Message}",
                    Attempts = 1,
                    ExhaustedAtUtc = DateTime.UtcNow,
                    SourceMessageId = TryGetEventIdHeader(result.Message.Headers),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(
                ex,
                "No se pudo publicar el mensaje poison del tópico del evento {EventType} (partición {Partition}, offset {Offset}) al tópico dead-letter; el offset se confirma igual (el mensaje queda descartado del lado consumidor).",
                _eventType,
                result.Partition.Value,
                result.Offset.Value);
        }
    }

    /// <summary>
    /// Intenta recuperar <see cref="KafkaIntegrationEventSerializer.EventIdHeader"/> del mensaje original
    /// para correlacionar el registro dead-letter con <c>IIntegrationEvent.EventId</c> — un mensaje
    /// poison no llegó a deserializarse, así que este header (copia de conveniencia escrita por
    /// <see cref="KafkaIntegrationEventSerializer.Serialize"/>, no el payload) es la única fuente posible;
    /// puede no estar presente si el mensaje ni siquiera fue producido por este serializador.
    /// </summary>
    private static string? TryGetEventIdHeader(Headers? headers)
    {
        if (headers is null)
        {
            return null;
        }

        return headers.TryGetLastBytes(KafkaIntegrationEventSerializer.EventIdHeader, out var bytes)
            ? Encoding.UTF8.GetString(bytes)
            : null;
    }

    public void Dispose()
    {
        // Close() abandona el grupo de consumidores de forma prolija (trigger inmediato de rebalance
        // en vez de esperar el session timeout) antes de liberar el handle nativo.
        _consumer.Close();
        _consumer.Dispose();
    }
}
