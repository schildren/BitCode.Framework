using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Instancias;

/// <summary>Evento de integración público: una <see cref="WorkflowTask"/> quedó asignada a un actor --
/// se levanta tanto al crear la tarea como al delegarla o escalarla (siempre que cambia
/// <see cref="WorkflowTask.AsignadoAUserId"/>). Un consumidor típico es Task Inbox/Notifications (Fase 6,
/// módulos 7 y 8) para avisarle al nuevo asignado. Registrado en <c>docs/catalogo-eventos.md</c> (regla
/// dura 27).</summary>
public sealed record TareaAsignadaIntegrationEvent(Guid WorkflowTaskId, Guid WorkflowInstanceId, Guid AsignadoAUserId)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Workflow.TareaAsignada";

    public int SchemaVersion => 1;

    public string PartitionKey => WorkflowInstanceId.ToString();
}
