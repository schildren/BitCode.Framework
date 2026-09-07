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

        services.TryAddSingleton(options);
        services.TryAddSingleton<IKafkaTopicNameResolver>(DefaultKafkaTopicNameResolver.Instance);

        services.TryAddSingleton<IProducer<string, byte[]>>(sp =>
        {
            var kafkaOptions = sp.GetRequiredService<KafkaMessagingOptions>();
            return new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(kafkaOptions)).Build();
        });

        services.TryAddSingleton<IEventPublisher, KafkaEventPublisher>();

        return services;
    }
}
