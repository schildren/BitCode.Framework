using BitCode.Framework.Shared.Application.Eventing;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

public static class KafkaServiceCollectionExtensions
{
    /// <summary>
    /// Registra el adapter Kafka de <see cref="IEventPublisher"/> (F3-02) leyendo
    /// <see cref="KafkaMessagingOptions"/> de la sección "Messaging:Kafka". Comparte un único
    /// <see cref="IProducer{TKey,TValue}"/> (singleton, thread-safe) para todo el proceso —
    /// <c>ServiceProvider</c> lo dispone al apagar la aplicación.
    /// </summary>
    /// <remarks>
    /// No registra ningún <see cref="KafkaEventConsumer{TEvent}"/>: a diferencia del productor, un
    /// consumidor está atado a un evento/tópico concreto (y a un handler <see cref="IEventConsumer{TEvent}"/>
    /// específico de cada bounded context), por lo que cada proyecto consumidor lo instancia
    /// explícitamente (constructor público, ver <see cref="KafkaEventConsumer{TEvent}"/>) en vez de que
    /// este método intente adivinar qué tipos de evento necesita suscribir. Coordinar esa suscripción
    /// con el ciclo de vida de un host (<c>IHostedService</c>) e Inbox es F3-04.
    /// </remarks>
    public static IServiceCollection AddSharedMessagingKafka(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(KafkaMessagingOptions.SectionName).Get<KafkaMessagingOptions>()
            ?? new KafkaMessagingOptions();

        // F3-11: falla en el arranque (no en el primer publish/consume) si la configuración de
        // seguridad de transporte es inconsistente (ver KafkaMessagingOptionsValidator).
        KafkaMessagingOptionsValidator.Validate(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IKafkaTopicNameResolver>(DefaultKafkaTopicNameResolver.Instance);

        services.TryAddSingleton<IProducer<string, byte[]>>(sp =>
        {
            var kafkaOptions = sp.GetRequiredService<KafkaMessagingOptions>();
            return new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(kafkaOptions)).Build();
        });

        services.TryAddSingleton<IEventPublisher, KafkaEventPublisher>();

        // F3-08 (DLQ): comparte el mismo IProducer<string, byte[]> singleton que KafkaEventPublisher —
        // publicar a un tópico dead-letter es, del lado del cliente Kafka, una publicación más (a otro
        // tópico), no una conexión/canal distinto.
        services.TryAddSingleton<IDeadLetterPublisher, KafkaDeadLetterPublisher>();

        // F3-07: clasificador de errores específico de Kafka para quien reintente publicaciones (por
        // ejemplo, OutboxBatchProcessor, Shared.Infrastructure.Persistence) — TryAddSingleton para que,
        // si AddSharedOutboxPublisher ya registró su clasificador de reserva (DefaultEventPublishFailureClassifier)
        // ANTES de llamar este método, gane igual el genérico (por eso ambas guías de registro piden
        // llamar AddSharedMessagingKafka primero); si se llama en el orden documentado, este es el que
        // efectivamente se resuelve.
        services.TryAddSingleton<IEventPublishFailureClassifier, KafkaEventPublishFailureClassifier>();

        // F9-05: health check de readiness del productor -- ver KafkaProducerHealthCheck para el
        // alcance exacto (metadatos del clúster, no un round-trip de publicación real).
        services.AddHealthChecks()
            .AddCheck<KafkaProducerHealthCheck>("kafka", tags: ["ready"]);

        return services;
    }
}
