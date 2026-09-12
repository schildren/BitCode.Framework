using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

/// <summary>
/// Evento de integración público del módulo Workflow -- el hecho de negocio "una versión de workflow
/// quedó publicada y disponible para iniciar instancias" cruza el límite de este bounded context (por
/// ejemplo, Task Inbox -- Fase 6, módulo 7, dependiente de Workflow -- puede querer precargar metadata del
/// nuevo grafo). Registrado en <c>docs/catalogo-eventos.md</c> (regla dura 27). Implementa
/// DELIBERADAMENTE tanto <see cref="DomainEvent"/> (para que <c>OutboxSaveChangesInterceptor</c> lo
/// recolecte de <c>WorkflowVersion</c>, tipo interno del módulo <c>BitCode.Platform.Workflow</c> -- no
/// referenciable desde este ensamblado de solo contratos, ver F9-02) como <see cref="IIntegrationEvent"/>
/// (para que <c>OutboxBatchProcessor</c> lo publique), mismo patrón que
/// <c>CatalogoVersionPublicadaIntegrationEvent</c> (Catalogs, Fase 6 módulo 3).
/// </summary>
public sealed record WorkflowVersionPublicadaIntegrationEvent(Guid WorkflowVersionId, Guid WorkflowDefinitionId, int Numero)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Workflow.WorkflowVersionPublicada";

    public int SchemaVersion => 1;

    /// <summary>Todos los eventos del mismo workflow (sucesivas versiones) quedan en la misma partición.</summary>
    public string PartitionKey => WorkflowDefinitionId.ToString();
}
