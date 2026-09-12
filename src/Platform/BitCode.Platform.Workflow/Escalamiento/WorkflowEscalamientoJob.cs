using BitCode.Framework.Platform.Workflow.Instancias;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace BitCode.Framework.Platform.Workflow.Escalamiento;

/// <summary>
/// Recorre periódicamente las <see cref="WorkflowTask"/> pendientes con SLA vencido y las reasigna al
/// actor de escalamiento del estado que las generó (Fase 6, módulo Workflow, "Timeout y SLA") -- un
/// consumidor real lo registra con <c>AddSharedBackgroundJobs</c> (F4-11, Quartz HA, ver
/// <c>docs/guia-quartz-ha.md</c>) para que un job lógico se dispare una única vez entre N réplicas:
/// <code>
/// services.AddSharedBackgroundJobs(
///     quartz =>
///     {
///         var jobKey = new JobKey("workflow-escalamiento");
///         quartz.AddJob&lt;WorkflowEscalamientoJob&gt;(j => j.WithIdentity(jobKey).RequestRecovery().StoreDurably());
///         quartz.AddTrigger(t => t.ForJob(jobKey).WithSimpleSchedule(s => s.WithIntervalInMinutes(1).RepeatForever()));
///     },
///     ha => ha.ConnectionString = connectionString);
/// </code>
/// <para>
/// Acceso directo a <see cref="WorkflowDbContext"/> con <c>IgnoreQueryFilters()</c> -- excepción legítima
/// documentada a la regla dura 1/5 (docs/convenciones.md), mismo precedente que
/// <c>OutboxBatchProcessor</c>/<c>EfIdempotencyStore</c> (Shared.Infrastructure.Persistence): este job es
/// un worker de infraestructura, no un handler de comando dentro del pipeline de MediatR/
/// <c>TransactionBehavior</c>, y necesita ver tareas de TODOS los tenants en cada disparo (el filtro
/// global de <c>MultiTenantDbContext</c> resuelve un único tenant por scope, que no existe para un job
/// sin <c>HttpContext</c>). Llama <c>SaveChangesAsync</c> explícitamente por la misma razón: no hay ningún
/// <c>TransactionBehavior</c> que lo haga por él.
/// </para>
/// <para>
/// Idempotente por diseño (regla dura 28): <see cref="WorkflowTask.Escalar"/> no repite el efecto sobre
/// una tarea ya escalada, así que una segunda ejecución de <see cref="Execute"/> sobre el mismo conjunto de
/// tareas vencidas (reintento de Quartz tras una caída a mitad de ciclo, <c>RequestRecovery()</c>, o dos
/// disparos que se solapan) no duplica la reasignación ni el evento de integración -- ver
/// <c>WorkflowEscalamientoJobTests</c> para la verificación de este caso puntual (ejecutar <see cref="Execute"/>
/// dos veces seguidas sobre el mismo estado de base de datos produce exactamente una fila de historial
/// "TareaEscalada" por tarea, no dos).
/// </para>
/// </summary>
public sealed class WorkflowEscalamientoJob(WorkflowDbContext dbContext) : IJob
{
    public Task Execute(IJobExecutionContext context) => EscalarVencidasAsync(context.CancellationToken);

    /// <summary>Lógica real del job, separada de <see cref="Execute"/> para poder probarla sin construir
    /// un <see cref="IJobExecutionContext"/> real de Quartz (interfaz grande, no diseñada para mockearse a
    /// mano) -- <c>WorkflowEscalamientoJobTests</c> llama a este método directamente, dos veces seguidas,
    /// para verificar la idempotencia documentada en la clase.</summary>
    public async Task EscalarVencidasAsync(CancellationToken cancellationToken)
    {
        var ahoraUtc = DateTime.UtcNow;

        // Reutiliza el mismo criterio que TareasVencidasSinEscalarSpecification (en vez de duplicarlo a
        // mano) para que el filtro viva en un único lugar aunque este job -- excepción documentada a la
        // regla dura 5 -- no pase por IReadRepository/ISpecification como el resto de las queries del
        // módulo.
        var criterio = new TareasVencidasSinEscalarSpecification(ahoraUtc).Criteria!;
        var vencidas = await dbContext.WorkflowTasks
            .IgnoreQueryFilters()
            .Where(criterio)
            .ToListAsync(cancellationToken);

        if (vencidas.Count == 0)
        {
            return;
        }

        var estadosPorId = await dbContext.WorkflowStates
            .IgnoreQueryFilters()
            .Where(s => vencidas.Select(t => t.WorkflowStateId).Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        foreach (var tarea in vencidas)
        {
            if (!estadosPorId.TryGetValue(tarea.WorkflowStateId, out var estado) || estado.EscalarAUserId is null)
            {
                continue;
            }

            if (!tarea.Escalar(estado.EscalarAUserId.Value))
            {
                continue;
            }

            // TenantId asignado explícitamente (no vía TenantSaveChangesInterceptor, que no tiene
            // ningún tenant "actual" que asumir en un job cross-tenant sin HttpContext) -- se toma del
            // mismo tenant que la tarea que originó el evento, leída con IgnoreQueryFilters más arriba.
            var historial = new WorkflowHistorial(
                Guid.NewGuid(), tarea.WorkflowInstanceId, "TareaEscalada",
                $"Tarea '{tarea.Titulo}' escalada por vencimiento de SLA a {estado.EscalarAUserId}.",
                actorUserId: null)
            {
                TenantId = tarea.TenantId,
            };
            dbContext.WorkflowHistoriales.Add(historial);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
