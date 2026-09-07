using BitCode.Framework.Shared.Application.Eventing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;

public static class OutboxPublisherServiceCollectionExtensions
{
    /// <summary>
    /// Registra el relay de Outbox (F3-03): <see cref="OutboxBatchProcessor"/> (scoped, reutiliza el
    /// mismo <c>DbContext</c> que <c>AddSharedPersistence&lt;TContext&gt;</c> ya registra) más el
    /// <see cref="OutboxPublisherBackgroundService"/> que lo invoca en loop. Requiere que ya exista un
    /// <see cref="BitCode.Framework.Shared.Application.Eventing.IEventPublisher"/> registrado (por
    /// ejemplo, <c>AddSharedMessagingKafka</c>, F3-02) — llamar este método DESPUÉS de
    /// <c>AddSharedPersistence&lt;TContext&gt;</c> y del registro del publisher concreto.
    /// </summary>
    /// <remarks>
    /// F3-07 (Retries): también registra, con <c>TryAddSingleton</c>, un
    /// <see cref="DefaultEventPublishFailureClassifier"/> de reserva para
    /// <see cref="IEventPublishFailureClassifier"/> — solo toma efecto si ningún adapter de broker
    /// concreto ya registró el suyo antes (por ejemplo, <c>AddSharedMessagingKafka</c> registra
    /// <c>KafkaEventPublishFailureClassifier</c>; si se llama antes que este método, como ya exige el
    /// orden documentado arriba, ese es el que gana). Sin este de reserva, un <see cref="IEventPublisher"/>
    /// sin adapter de broker Kafka dejaría a <see cref="OutboxBatchProcessor"/> sin poder resolver esa
    /// dependencia.
    /// </remarks>
    public static IServiceCollection AddSharedOutboxPublisher(
        this IServiceCollection services,
        Action<OutboxPublisherOptions>? configureOptions = null)
    {
        var options = new OutboxPublisherOptions();
        configureOptions?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IEventPublishFailureClassifier, DefaultEventPublishFailureClassifier>();
        services.TryAddScoped<OutboxBatchProcessor>();
        services.AddHostedService<OutboxPublisherBackgroundService>();

        return services;
    }
}
