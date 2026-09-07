namespace BitCode.Framework.Shared.Application.Eventing;

/// <summary>
/// Clasificador de reserva (F3-07) usado cuando ningún adapter de broker concreto registró el suyo
/// propio (por ejemplo, <c>OutboxBatchProcessor</c> sin <c>AddSharedMessagingKafka</c> registrado
/// todavía, o un futuro <see cref="IEventPublisher"/> distinto de Kafka sin clasificador dedicado).
/// </summary>
/// <remarks>
/// Sin ninguna señal específica del broker (código de error, propiedad "es fatal", etc.), esta
/// implementación clasifica TODO como <see cref="EventPublishFailureKind.Transient"/> — el valor por
/// defecto más seguro: nunca descarta/agota un evento sin evidencia real de que reintentar sea inútil.
/// El límite máximo de reintentos (<see cref="EventRetryPolicyOptions.MaxAttempts"/>) sigue acotando el
/// reintento indefinido incluso en este caso, así que un error genuinamente permanente igual termina
/// marcado como agotado — solo tarda todos los intentos configurados en lugar de detectarse antes.
/// </remarks>
public sealed class DefaultEventPublishFailureClassifier : IEventPublishFailureClassifier
{
    public EventPublishFailureKind Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return EventPublishFailureKind.Transient;
    }
}
