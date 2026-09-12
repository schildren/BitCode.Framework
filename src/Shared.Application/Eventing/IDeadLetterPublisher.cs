namespace BitCode.Framework.Shared.Application.Eventing;

/// <summary>
/// Contrato de publicación a una cola de mensajes muertos ("dead-letter", F3-08), agnóstico del broker
/// concreto (Kafka u otro, ADR <c>docs/adr/0005-mensajeria-kafka.md</c>) — mismo criterio de diseño que
/// <see cref="IEventPublisher"/> (F3-01): el lado emisor (<c>OutboxBatchProcessor</c>,
/// <c>Shared.Infrastructure.Persistence</c>) y el lado consumidor (<c>KafkaEventConsumer{TEvent}</c>,
/// <c>Shared.Infrastructure.Messaging.Kafka</c>) necesitan poder señalizar "este mensaje agotó sus
/// reintentos" sin depender de ningún paquete de broker concreto.
/// </summary>
/// <remarks>
/// Punto de extensión explícito para F3-08 (DLQ): F3-07 dejó dos señales de agotamiento
/// (<c>OutboxMessage.ExhaustedAtUtc</c> del lado emisor, <see cref="EventProcessingExhaustedException"/>
/// del lado consumidor) sin ningún enrutamiento a un tópico de mensajes muertos real — esta interfaz es
/// ese enrutamiento. La implementación concreta (<c>KafkaDeadLetterPublisher</c>, F3-02/F3-08) decide a
/// qué tópico/nombre publica y qué headers agrega; este contrato solo declara la operación en sí, igual
/// que <see cref="IEventPublisher"/> no decide tópico/partición.
/// </remarks>
public interface IDeadLetterPublisher
{
    /// <summary>Publica una copia de un mensaje agotado a la cola de mensajes muertos correspondiente.</summary>
    Task PublishAsync(DeadLetterEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Metadatos y payload de un mensaje que agotó su margen de reintentos (F3-07) y se enruta a DLQ (F3-08).
/// Deliberadamente NO exige un <see cref="IIntegrationEvent"/> ya reconstruido: el lado emisor
/// (<c>OutboxBatchProcessor</c>) puede necesitar enviar a DLQ un <c>OutboxMessage</c> cuyo
/// <c>EventType</c> ni siquiera pudo resolverse a un tipo .NET (deserialización imposible, error
/// PERMANENTE por definición) — en ese caso no existe ninguna instancia de <see cref="IIntegrationEvent"/>
/// que envolver, solo el payload crudo y el nombre lógico/técnico del tipo que se intentó resolver.
/// </summary>
public sealed class DeadLetterEnvelope
{
    /// <summary>
    /// Nombre lógico del evento cuando se conoce (<c>IIntegrationEvent.EventType</c>), o el identificador
    /// técnico disponible cuando no (por ejemplo, <c>OutboxMessage.EventType</c>,
    /// <c>Type.AssemblyQualifiedName</c>, si el evento nunca llegó a reconstruirse como
    /// <see cref="IIntegrationEvent"/>) — usado por la implementación concreta para resolver el tópico
    /// dead-letter de destino.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>Payload original del mensaje (mismos bytes que se intentaron publicar/procesar), sin transformar.</summary>
    public required byte[] Payload { get; init; }

    /// <summary>Motivo del último fallo (mensaje de la excepción que agotó el margen de reintentos).</summary>
    public required string Reason { get; init; }

    /// <summary>Cantidad total de intentos realizados antes de agotar el margen.</summary>
    public required int Attempts { get; init; }

    /// <summary>Momento (UTC) en que se marcó el mensaje como agotado.</summary>
    public required DateTime ExhaustedAtUtc { get; init; }

    /// <summary>
    /// Identificador del mensaje origen para correlación con la fuente (<c>OutboxMessage.Id</c> del lado
    /// emisor, o <c>IIntegrationEvent.EventId</c> del lado consumidor) — <see langword="null"/> si no
    /// aplica.
    /// </summary>
    public string? SourceMessageId { get; init; }
}
