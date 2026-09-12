using BitCode.Framework.Platform.TaskInbox.Bandeja;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Persistence;

namespace BitCode.Framework.Platform.TaskInbox.Eventos;

/// <summary>Ver remarks de <see cref="TareaAsignadaIntegrationEventConsumer"/> para el mecanismo de
/// invocación (Inbox, F1-24/F3-04). Si la fila todavía no existe (ver
/// <see cref="TaskInboxItem.CrearYaResuelta"/> para el caso de borde de orden entre tópicos), la crea
/// directamente en estado <see cref="TaskInboxEstado.Aprobada"/>.</summary>
internal sealed class TareaAprobadaIntegrationEventConsumer(IRepository<TaskInboxItem, Guid> repository)
    : IEventConsumer<TareaAprobadaIntegrationEvent>
{
    public async Task ConsumeAsync(TareaAprobadaIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var item = await repository.GetByIdAsync(integrationEvent.WorkflowTaskId, cancellationToken);
        if (item is null)
        {
            await repository.AddAsync(
                TaskInboxItem.CrearYaResuelta(
                    integrationEvent.WorkflowTaskId, integrationEvent.WorkflowInstanceId,
                    TaskInboxEstado.Aprobada, integrationEvent.ResueltaPorUserId, DateTime.UtcNow),
                cancellationToken);
            return;
        }

        item.AplicarAprobacion(integrationEvent.ResueltaPorUserId, DateTime.UtcNow);
        repository.Update(item);
    }
}
