using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Instancias;

/// <summary>Evento de integración público: una instancia alcanzó un estado final. Registrado en
/// <c>docs/catalogo-eventos.md</c> (regla dura 27).</summary>
public sealed record WorkflowInstanciaFinalizadaIntegrationEvent(
    Guid WorkflowInstanceId, Guid WorkflowDefinitionId, Guid EstadoFinalId)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Workflow.WorkflowInstanciaFinalizada";

    public int SchemaVersion => 1;

    public string PartitionKey => WorkflowInstanceId.ToString();
}
