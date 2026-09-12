using BitCode.Framework.Platform.Notifications.Actors;
using BitCode.Framework.Platform.Notifications.Envio;
using BitCode.Framework.Platform.Notifications.Envio.Canales;
using BitCode.Framework.Platform.Notifications.Eventos;
using BitCode.Framework.Platform.Notifications.HealthChecks;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Application.Eventing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Platform.Notifications;

/// <summary>
/// Envuelve el cableado de aplicación del módulo Notifications (Fase 6, módulo 8) -- mismo espíritu que
/// <c>TaskInboxServiceCollectionExtensions</c>. Un consumidor real llama
/// <c>services.AddSharedPersistence&lt;NotificationsDbContext&gt;(connectionString)</c> directamente en
/// su propio <c>InfrastructureModule</c> (ver <c>Sample.Notifications.Api</c>).
/// </summary>
/// <remarks>
/// Registra los DOS canales de referencia (<see cref="EmailNotificationChannelSender"/>,
/// <see cref="InAppNotificationChannelSender"/>) y el consumidor de EJEMPLO de
/// <c>Workflow.TareaAsignada</c> como <c>Scoped</c> (mismo ciclo de vida que
/// <c>IInboxMessageProcessor</c>/el <c>DbContext</c> de turno, requisito documentado en
/// <c>docs/guia-inbox-consumer.md</c>) -- NO registra <c>NotificationRetryJob</c> (un consumidor que lo
/// necesite lo agrega explícitamente con <c>AddSharedBackgroundJobs</c>, F4-11, mismo criterio que
/// <c>WorkflowEscalamientoJob</c>) ni ningún <c>KafkaEventConsumer&lt;TEvent&gt;</c>/host que invoque el
/// consumidor de ejemplo contra un broker real (mismo estado que el resto de Fase 6, ver
/// <c>docs/catalogo-eventos.md</c>).
/// </remarks>
public static class NotificationsServiceCollectionExtensions
{
    /// <param name="configureSmtp">
    /// Configuración del canal Email (<see cref="SmtpOptions"/>) -- obligatorio proveerla explícitamente
    /// (sin valor "mágico" por defecto que apunte a un servidor real), mismo criterio que
    /// <c>AddSharedDocuments(configureBlobStore)</c> con <c>DocumentBlobStoreOptions</c>.
    /// </param>
    /// <param name="configureOptions">
    /// Configuración opcional de <see cref="NotificationsOptions"/> (política de reintentos, F3-07
    /// reutilizado) -- si no se provee, aplican los valores por defecto de <c>EventRetryPolicyOptions</c>.
    /// </param>
    public static IServiceCollection AddSharedNotifications(
        this IServiceCollection services,
        Action<SmtpOptions> configureSmtp,
        Action<NotificationsOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(configureSmtp);

        services.AddHttpContextAccessor();
        services.TryAddScoped<INotificationsActorContext, HttpContextNotificationsActorContext>();

        services.AddOptions<SmtpOptions>().Configure(configureSmtp);
        services.AddOptions<NotificationsOptions>().Configure(configureOptions ?? (_ => { }));

        services.TryAddScoped<INotificationSender, NotificationSender>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INotificationChannelSender, EmailNotificationChannelSender>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INotificationChannelSender, InAppNotificationChannelSender>());

        // Ejemplo de referencia (ver Eventos/TareaAsignadaNotificationEventConsumer.cs) -- consume el
        // mismo evento público de Workflow que ya consume Task Inbox, sin depender de TaskInboxDbContext
        // ni de ningún otro dato de ese módulo.
        services.TryAddScoped<IEventConsumer<TareaAsignadaIntegrationEvent>, TareaAsignadaNotificationEventConsumer>();

        services.AddHealthChecks()
            .AddCheck<NotificationsDbContextHealthCheck>("sql-server-notifications", tags: ["ready"]);

        return services;
    }
}
