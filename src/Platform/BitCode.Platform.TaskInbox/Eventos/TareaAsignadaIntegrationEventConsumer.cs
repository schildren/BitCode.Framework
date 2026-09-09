using BitCode.Framework.Platform.TaskInbox.Bandeja;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Persistence;

namespace BitCode.Framework.Platform.TaskInbox.Eventos;

/// <summary>
/// Construye/actualiza el read-model propio de Task Inbox a partir de
/// <see cref="TareaAsignadaIntegrationEvent"/> -- el ÚNICO efecto de negocio que la asignación
/// (alta, delegación o escalamiento de una <c>WorkflowTask</c>) produce en este módulo. Pensado para
/// ser invocado exclusivamente a través de <c>IInboxMessageProcessor.ProcessAsync</c> (F1-24/F3-04,
/// ver <c>docs/guia-inbox-consumer.md</c>), nunca directamente -- así una reentrega del mismo evento
/// (mismo <see cref="IIntegrationEvent.EventId"/>) no repite el efecto.
/// </summary>
/// <remarks>
/// No modifica NUNCA <c>WorkflowDbContext</c> ni ningún dato de Workflow: solo su propia tabla
/// <c>TaskInboxItems</c>, vía <see cref="IRepository{TEntity,TId}"/> igual que cualquier handler de
/// comando de este framework (regla dura 1, <c>docs/convenciones.md</c>) -- la persistencia real la
/// cierra <c>IInboxMessageProcessor.ProcessAsync</c> con un único <c>SaveChangesAsync</c> que incluye,
/// en la misma transacción, tanto el efecto de negocio de este método como la marca de Inbox
/// procesado.
/// </remarks>
internal sealed class TareaAsignadaIntegrationEventConsumer(IRepository<TaskInboxItem, Guid> repository)
    : IEventConsumer<TareaAsignadaIntegrationEvent>
{
    public async Task ConsumeAsync(TareaAsignadaIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var item = await repository.GetByIdAsync(integrationEvent.WorkflowTaskId, cancellationToken);
        if (item is null)
        {
            await repository.AddAsync(
                new TaskInboxItem(
                    integrationEvent.WorkflowTaskId, integrationEvent.WorkflowInstanceId,
                    integrationEvent.AsignadoAUserId, DateTime.UtcNow),
                cancellationToken);
            return;
        }

        item.AplicarAsignacion(integrationEvent.AsignadoAUserId, DateTime.UtcNow);
        repository.Update(item);
    }
}
