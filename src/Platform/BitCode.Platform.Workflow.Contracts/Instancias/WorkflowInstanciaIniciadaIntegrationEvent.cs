using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Instancias;

/// <summary>Evento de integración público: se inició una nueva instancia de un workflow -- por ejemplo,
/// Task Inbox (Fase 6, módulo 7) puede suscribirse para saber que hay actividad nueva sin sondear.
/// Registrado en <c>docs/catalogo-eventos.md</c> (regla dura 27).</summary>
public sealed record WorkflowInstanciaIniciadaIntegrationEvent(
    Guid WorkflowInstanceId, Guid WorkflowDefinitionId, Guid WorkflowVersionId, Guid IniciadoPorUserId)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Workflow.WorkflowInstanciaIniciada";

    public int SchemaVersion => 1;

    /// <summary>Todos los eventos de la misma instancia (inicio, finalización) quedan en la misma
    /// partición -- un consumidor que necesite verlos en orden lo obtiene sin trabajo adicional.</summary>
    public string PartitionKey => WorkflowInstanceId.ToString();
}
