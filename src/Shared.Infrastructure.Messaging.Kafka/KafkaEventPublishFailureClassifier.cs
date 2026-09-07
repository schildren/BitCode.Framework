using BitCode.Framework.Shared.Application.Eventing;
using Confluent.Kafka;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Clasificación de errores de publicación específica de Kafka (F3-07): distingue un
/// <see cref="ProduceException{TKey,TValue}"/> transitorio (broker temporalmente no disponible, timeout
/// de red, réplicas insuficientes momentáneamente) de uno permanente (mensaje demasiado grande,
/// inválido, sin autorización) usando <see cref="Error.IsFatal"/> y <see cref="Error.Code"/>
/// (<c>Confluent.Kafka.ErrorCode</c>, que refleja 1:1 los códigos de error del protocolo Kafka).
/// </summary>
public sealed class KafkaEventPublishFailureClassifier : IEventPublishFailureClassifier
{
    public EventPublishFailureKind Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // El productor de este framework siempre usa Message<string, byte[]> (ver
        // KafkaEventPublisher/KafkaIntegrationEventSerializer) — ProduceException<string, byte[]> es la
        // única forma concreta que Confluent.Kafka lanza para un fallo de ProduceAsync.
        if (exception is ProduceException<string, byte[]> produceException)
        {
            return ClassifyKafkaError(produceException.Error);
        }

        // Cualquier otra excepción (por ejemplo, de serialización antes de llegar al broker) sin
        // información específica de Kafka: se asume transitoria por seguridad — nunca se descarta un
        // evento sin evidencia real de que reintentar sea inútil. EventRetryPolicyOptions.MaxAttempts
        // sigue acotando el reintento indefinido en cualquier caso.
        return EventPublishFailureKind.Transient;
    }

    private static EventPublishFailureKind ClassifyKafkaError(Error error)
    {
        // Un error "fatal" de Confluent.Kafka indica que el productor/cliente quedó en un estado del
        // que no se puede recuperar reintentando la misma operación (por ejemplo, un fencing de
        // productor transaccional) — no hay ambigüedad posible, siempre permanente.
        if (error.IsFatal)
        {
            return EventPublishFailureKind.Permanent;
        }

        return error.Code switch
        {
            // El mensaje en sí es el problema (tamaño, formato, contenido inválido) — reintentar
            // exactamente el mismo mensaje contra el mismo broker nunca va a cambiar el resultado.
            ErrorCode.MsgSizeTooLarge => EventPublishFailureKind.Permanent,
            ErrorCode.InvalidMsg => EventPublishFailureKind.Permanent,
            ErrorCode.InvalidMsgSize => EventPublishFailureKind.Permanent,
            ErrorCode.RecordListTooLarge => EventPublishFailureKind.Permanent,
            ErrorCode.UnsupportedForMessageFormat => EventPublishFailureKind.Permanent,
            ErrorCode.InvalidRequiredAcks => EventPublishFailureKind.Permanent,

            // Falta de autorización/configuración del tópico — un reintento inmediato con las mismas
            // credenciales/tópico tampoco va a cambiar el resultado (requiere intervención externa,
            // distinta de "esperar a que el broker se recupere").
            ErrorCode.TopicAuthorizationFailed => EventPublishFailureKind.Permanent,
            ErrorCode.ClusterAuthorizationFailed => EventPublishFailureKind.Permanent,
            ErrorCode.TopicException => EventPublishFailureKind.Permanent,

            // Cualquier otro código (BrokerNotAvailable, RequestTimedOut, NetworkException,
            // NotEnoughReplicas, Local_Transport, Local_TimedOut, Local_AllBrokersDown, etc.): condición
            // del broker/red que puede resolverse sola con el paso del tiempo — transitorio.
            _ => EventPublishFailureKind.Transient,
        };
    }
}
