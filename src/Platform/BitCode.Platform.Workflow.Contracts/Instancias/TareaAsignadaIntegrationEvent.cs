using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Instancias;

/// <summary>Evento de integración público: una tarea de Workflow (<c>WorkflowTask</c>, tipo interno del
/// módulo <c>BitCode.Platform.Workflow</c> -- no referenciable desde este ensamblado de solo contratos,
/// ver F9-02) quedó asignada a un actor -- se levanta tanto al crear la tarea como al delegarla o
/// escalarla (siempre que cambia el usuario asignado). Un consumidor típico es Task Inbox/Notifications
/// (Fase 6, módulos 7 y 8) para avisarle al nuevo asignado. Registrado en <c>docs/catalogo-eventos.md</c>
/// (regla dura 27).</summary>
public sealed record TareaAsignadaIntegrationEvent(Guid WorkflowTaskId, Guid WorkflowInstanceId, Guid AsignadoAUserId)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Workflow.TareaAsignada";

    public int SchemaVersion => 1;

    public string PartitionKey => WorkflowInstanceId.ToString();
}
