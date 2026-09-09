using BitCode.Framework.Platform.TaskInbox.Actors;
using BitCode.Framework.Platform.TaskInbox.Eventos;
using BitCode.Framework.Platform.TaskInbox.HealthChecks;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Application.Eventing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Platform.TaskInbox;

/// <summary>
/// Envuelve el cableado de aplicación del módulo Task Inbox (Fase 6, módulo 7) -- mismo espíritu que
/// <c>WorkflowServiceCollectionExtensions</c>. Un consumidor real llama
/// <c>services.AddSharedPersistence&lt;TaskInboxDbContext&gt;(connectionString)</c> directamente en su
/// propio <c>InfrastructureModule</c> (ver <c>Sample.TaskInbox.Api</c>).
/// </summary>
/// <remarks>
/// Registra los tres <see cref="IEventConsumer{TEvent}"/> de este módulo como <c>Scoped</c> (mismo
/// ciclo de vida que <c>IInboxMessageProcessor</c>/el <c>DbContext</c> de turno, requisito documentado
/// en <c>docs/guia-inbox-consumer.md</c>) -- NO registra ningún <c>KafkaEventConsumer&lt;TEvent&gt;</c>
/// ni ningún host que los invoque contra un broker real: mismo estado que el resto de los módulos de
/// Fase 6 (ningún evento de integración productivo de esta plataforma se publica hoy contra Kafka
/// real, ver <c>docs/catalogo-eventos.md</c>). Un consumidor productivo que sí tenga Kafka wireado
/// (<c>AddSharedMessagingKafka</c>, F3-02) instanciaría <c>KafkaEventConsumer&lt;TareaAsignadaIntegrationEvent&gt;</c>
/// (y los otros dos) contra los tópicos <c>Workflow.TareaAsignada</c>/<c>Workflow.TareaAprobada</c>/
/// <c>Workflow.TareaRechazada</c>, resolviendo estos mismos <see cref="IEventConsumer{TEvent}"/> del
/// mismo contenedor de DI -- sin cambiar una línea de este módulo.
/// </remarks>
public static class TaskInboxServiceCollectionExtensions
{
    public static IServiceCollection AddSharedTaskInbox(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<ITaskInboxActorContext, HttpContextTaskInboxActorContext>();

        services.TryAddScoped<IEventConsumer<TareaAsignadaIntegrationEvent>, TareaAsignadaIntegrationEventConsumer>();
        services.TryAddScoped<IEventConsumer<TareaAprobadaIntegrationEvent>, TareaAprobadaIntegrationEventConsumer>();
        services.TryAddScoped<IEventConsumer<TareaRechazadaIntegrationEvent>, TareaRechazadaIntegrationEventConsumer>();

        services.AddHealthChecks()
            .AddCheck<TaskInboxDbContextHealthCheck>("sql-server-taskinbox", tags: ["ready"]);

        return services;
    }
}
