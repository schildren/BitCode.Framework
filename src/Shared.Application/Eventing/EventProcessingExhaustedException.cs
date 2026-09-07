namespace BitCode.Framework.Shared.Application.Eventing;

/// <summary>
/// F3-07: excepción lanzada cuando el procesamiento de un evento de integración agotó
/// <see cref="EventRetryPolicyOptions.MaxAttempts"/> (o el error se clasificó como
/// <see cref="EventPublishFailureKind.Permanent"/>) sin haber tenido éxito. Es el punto de extensión
/// explícito que F3-08 (DLQ) necesita: un llamador que quiera enrutar el mensaje a una cola de mensajes
/// muertos en vez de seguir reintentando indefinidamente puede capturar específicamente este tipo (por
/// ejemplo, en el host que llama <c>KafkaEventConsumer&lt;TEvent&gt;.ConsumeAndHandleOnceAsync</c>) sin
/// tener que inspeccionar el mensaje/excepción original para decidirlo.
/// </summary>
/// <remarks>
/// Esta tarea (F3-07) NO implementa ningún enrutamiento a una cola de mensajes muertos real: lanzar esta
/// excepción es, hoy, equivalente a cualquier otra excepción no manejada (el offset de Kafka no se
/// confirma, el mensaje se reintenta en la próxima entrega, ver <c>KafkaEventConsumer&lt;TEvent&gt;</c>)
/// — la única diferencia observable es el tipo de excepción, que F3-08 puede usar para bifurcar el
/// comportamiento sin cambiar la firma pública de nada más.
/// </remarks>
public sealed class EventProcessingExhaustedException : Exception
{
    public EventProcessingExhaustedException(string messageId, string eventType, int attempts, Exception innerException)
        : base(
            $"El procesamiento del evento '{eventType}' (mensaje {messageId}) agotó {attempts} intento(s) sin éxito.",
            innerException)
    {
        MessageId = messageId;
        EventType = eventType;
        Attempts = attempts;
    }

    /// <summary>Identificador del mensaje agotado (típicamente <c>IIntegrationEvent.EventId</c> como string).</summary>
    public string MessageId { get; }

    /// <summary><c>IIntegrationEvent.EventType</c> del evento agotado.</summary>
    public string EventType { get; }

    /// <summary>Cantidad de intentos totales realizados antes de agotar el límite.</summary>
    public int Attempts { get; }
}
