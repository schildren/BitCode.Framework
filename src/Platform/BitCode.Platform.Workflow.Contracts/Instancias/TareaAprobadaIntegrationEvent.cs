using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Instancias;

/// <summary>Evento de integración público: una <see cref="WorkflowTask"/> fue aprobada. Registrado en
/// <c>docs/catalogo-eventos.md</c> (regla dura 27).</summary>
public sealed record TareaAprobadaIntegrationEvent(Guid WorkflowTaskId, Guid WorkflowInstanceId, Guid ResueltaPorUserId)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Workflow.TareaAprobada";

    public int SchemaVersion => 1;

    public string PartitionKey => WorkflowInstanceId.ToString();
}
