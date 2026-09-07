namespace BitCode.Framework.Shared.Application.Eventing;

/// <summary>
/// Clasifica una excepción producida al publicar/procesar un <see cref="IIntegrationEvent"/> como
/// <see cref="EventPublishFailureKind.Transient"/> o <see cref="EventPublishFailureKind.Permanent"/>
/// (F3-07). Cada adapter de broker concreto (por ejemplo, <c>KafkaEventPublishFailureClassifier</c> en
/// <c>Shared.Infrastructure.Messaging.Kafka</c>) provee su propia implementación con conocimiento de las
/// excepciones/códigos de error específicos de ese broker — este contrato vive en
/// <c>Shared.Application.Eventing</c> para que consumidores como <c>OutboxBatchProcessor</c>
/// (<c>Shared.Infrastructure.Persistence</c>) puedan depender de la clasificación sin referenciar
/// ningún paquete de broker concreto (mismo criterio de desacople que <see cref="IEventPublisher"/>).
/// </summary>
public interface IEventPublishFailureClassifier
{
    /// <summary>
    /// Clasifica <paramref name="exception"/>. Una implementación sin información suficiente para
    /// decidir debe devolver <see cref="EventPublishFailureKind.Transient"/> — el valor por defecto
    /// más seguro: nunca abandona un evento sin evidencia real de que reintentar sea inútil (el límite
    /// máximo de <see cref="EventRetryPolicyOptions.MaxAttempts"/> sigue acotando el reintento
    /// indefinido en cualquier caso).
    /// </summary>
    EventPublishFailureKind Classify(Exception exception);
}
