using BitCode.Framework.Platform.Reporting.HealthChecks;
using BitCode.Framework.Platform.Reporting.WorkflowInstancias;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Application.Eventing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Platform.Reporting;

/// <summary>
/// Envuelve el cableado de aplicación del módulo Reporting (Fase 6, módulo 11) -- mismo espíritu que
/// <c>TaskInboxServiceCollectionExtensions</c>. Un consumidor real llama
/// <c>services.AddSharedPersistence&lt;ReportingDbContext&gt;(connectionString)</c> directamente en su
/// propio <c>InfrastructureModule</c> (ver <c>Sample.Reporting.Api</c>).
/// </summary>
/// <remarks>
/// Registra los dos <see cref="IEventConsumer{TEvent}"/> del ejemplo de referencia como <c>Scoped</c>
/// (mismo ciclo de vida que <c>IInboxMessageProcessor</c>/el <c>DbContext</c> de turno, requisito
/// documentado en <c>docs/guia-inbox-consumer.md</c>) -- NO registra ningún
/// <c>KafkaEventConsumer&lt;TEvent&gt;</c> ni ningún host que los invoque contra un broker real: mismo
/// estado que el resto de los módulos de Fase 6 (ningún evento de integración productivo de esta
/// plataforma se publica hoy contra Kafka real, ver <c>docs/catalogo-eventos.md</c>). Un módulo futuro que
/// agregue su propio read-model de reporting (ver "Cómo agregar un read-model nuevo" en
/// <c>docs/guia-reporting.md</c>) registra sus propios <see cref="IEventConsumer{TEvent}"/> de la misma
/// forma, sin tocar este método.
/// </remarks>
public static class ReportingServiceCollectionExtensions
{
    public static IServiceCollection AddSharedReporting(this IServiceCollection services)
    {
        services.TryAddScoped<IEventConsumer<WorkflowInstanciaIniciadaIntegrationEvent>, WorkflowInstanciaIniciadaIntegrationEventConsumer>();
        services.TryAddScoped<IEventConsumer<WorkflowInstanciaFinalizadaIntegrationEvent>, WorkflowInstanciaFinalizadaIntegrationEventConsumer>();

        services.AddHealthChecks()
            .AddCheck<ReportingDbContextHealthCheck>("sql-server-reporting", tags: ["ready"]);

        return services;
    }
}
