using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Persistence;
using Microsoft.Extensions.Logging;

namespace BitCode.Framework.Platform.Reporting.WorkflowInstancias;

/// <summary>
/// Construye/actualiza el read-model propio de Reporting a partir de
/// <see cref="WorkflowInstanciaIniciadaIntegrationEvent"/> -- ejemplo de integración de referencia (ver
/// <c>docs/guia-reporting.md</c>). Pensado para ser invocado exclusivamente a través de
/// <c>IInboxMessageProcessor.ProcessAsync</c> (F1-24/F3-04, ver <c>docs/guia-inbox-consumer.md</c>), nunca
/// directamente -- así una reentrega del mismo evento (mismo <see cref="IIntegrationEvent.EventId"/>) no
/// repite el efecto.
/// </summary>
/// <remarks>
/// No modifica NUNCA <c>WorkflowDbContext</c> ni ningún dato de Workflow: solo su propia tabla
/// <c>ReporteWorkflowInstancias</c>, vía <see cref="IRepository{TEntity,TId}"/> igual que cualquier handler
/// de comando de este framework (regla dura 1, <c>docs/convenciones.md</c>) -- la persistencia real la
/// cierra <c>IInboxMessageProcessor.ProcessAsync</c> con un único <c>SaveChangesAsync</c> que incluye, en la
/// misma transacción, tanto el efecto de negocio de este método como la marca de Inbox procesado.
/// </remarks>
internal sealed class WorkflowInstanciaIniciadaIntegrationEventConsumer(
    IRepository<ReporteWorkflowInstancia, Guid> repository,
    ILogger<WorkflowInstanciaIniciadaIntegrationEventConsumer> logger)
    : IEventConsumer<WorkflowInstanciaIniciadaIntegrationEvent>
{
    public async Task ConsumeAsync(
        WorkflowInstanciaIniciadaIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var reporte = await repository.GetByIdAsync(integrationEvent.WorkflowInstanceId, cancellationToken);
        if (reporte is null)
        {
            var nuevo = new ReporteWorkflowInstancia(
                integrationEvent.WorkflowInstanceId, integrationEvent.WorkflowDefinitionId);
            nuevo.AplicarInicio(integrationEvent.OccurredOnUtc);
            WorkflowInstanciaFinalizadaIntegrationEventConsumer.AdvertirSiDuracionQuedoSinCalcular(nuevo, logger);
            await repository.AddAsync(nuevo, cancellationToken);
            return;
        }

        // Caso de borde documentado en ReporteWorkflowInstancia.remarks: la finalización pudo llegar
        // primero y crear la fila -- este evento tardío solo completa IniciadaAtUtc/DuracionSegundos, sin
        // revertir el estado ya finalizado.
        reporte.AplicarInicio(integrationEvent.OccurredOnUtc);
        WorkflowInstanciaFinalizadaIntegrationEventConsumer.AdvertirSiDuracionQuedoSinCalcular(reporte, logger);
        repository.Update(reporte);
    }
}
