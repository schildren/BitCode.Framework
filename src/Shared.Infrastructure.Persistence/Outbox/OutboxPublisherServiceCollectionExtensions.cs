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
    public static IServiceCollection AddSharedOutboxPublisher(
        this IServiceCollection services,
        Action<OutboxPublisherOptions>? configureOptions = null)
    {
        var options = new OutboxPublisherOptions();
        configureOptions?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddScoped<OutboxBatchProcessor>();
        services.AddHostedService<OutboxPublisherBackgroundService>();

        return services;
    }
}
