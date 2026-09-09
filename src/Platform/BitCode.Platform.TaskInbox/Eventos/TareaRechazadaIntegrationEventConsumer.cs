using BitCode.Framework.Platform.TaskInbox.Bandeja;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Persistence;

namespace BitCode.Framework.Platform.TaskInbox.Eventos;

/// <summary>Ver remarks de <see cref="TareaAsignadaIntegrationEventConsumer"/>.</summary>
internal sealed class TareaRechazadaIntegrationEventConsumer(IRepository<TaskInboxItem, Guid> repository)
    : IEventConsumer<TareaRechazadaIntegrationEvent>
{
    public async Task ConsumeAsync(TareaRechazadaIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var item = await repository.GetByIdAsync(integrationEvent.WorkflowTaskId, cancellationToken);
        if (item is null)
        {
            await repository.AddAsync(
                TaskInboxItem.CrearYaResuelta(
                    integrationEvent.WorkflowTaskId, integrationEvent.WorkflowInstanceId,
                    TaskInboxEstado.Rechazada, integrationEvent.ResueltaPorUserId, DateTime.UtcNow),
                cancellationToken);
            return;
        }

        item.AplicarRechazo(integrationEvent.ResueltaPorUserId, DateTime.UtcNow);
        repository.Update(item);
    }
}
