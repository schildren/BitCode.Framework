using BitCode.Framework.Platform.Workflow.Actors;
using BitCode.Framework.Platform.Workflow.HealthChecks;
using BitCode.Framework.Platform.Workflow.Instancias;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Platform.Workflow;

/// <summary>
/// Envuelve el cableado de aplicación del módulo Workflow (Fase 6, módulo 6) -- mismo espíritu que
/// <c>CatalogsServiceCollectionExtensions</c>. Un consumidor real llama
/// <c>services.AddSharedPersistence&lt;WorkflowDbContext&gt;(connectionString)</c> directamente en su
/// propio <c>InfrastructureModule</c> (ver <c>Sample.Workflow.Api</c>), que ya deja resueltos
/// <c>IRepository&lt;,&gt;</c>/<c>IReadRepository&lt;,&gt;</c>/<c>IUnitOfWork</c>/<c>IIdempotencyStore</c>/
/// <c>IInboxStore</c> para las siete entidades de este módulo (genéricos, sin registro adicional por
/// tipo) y el health check "sql-server" de lectura genérico. El escalamiento por SLA
/// (<c>WorkflowEscalamientoJob</c>) NO se registra automáticamente acá -- un consumidor que lo necesite lo
/// agrega explícitamente con <c>AddSharedBackgroundJobs</c> (F4-11), ver <c>docs/guia-workflow.md</c>,
/// sección "Timeout y SLA": mismo criterio que el resto del framework, que nunca activa Quartz por su
/// cuenta detrás de un `AddSharedX`.
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddSharedWorkflow(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IWorkflowActorContext, HttpContextWorkflowActorContext>();
        services.TryAddScoped<IWorkflowEngine, WorkflowEngine>();

        services.AddHealthChecks()
            .AddCheck<WorkflowDbContextHealthCheck>("sql-server-workflow", tags: ["ready"]);

        return services;
    }
}
