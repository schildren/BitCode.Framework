using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Documents.Documentos;

/// <summary>
/// Evento de integración público del módulo Documents -- el hecho de negocio "se cargó un documento
/// nuevo" (primera versión) cruza el límite de este bounded context: por ejemplo, Import and Export
/// (Fase 6, módulo 10, depende explícitamente de Documents) puede reaccionar sin sondear el endpoint de
/// lectura. Registrado en <c>docs/catalogo-eventos.md</c> (regla dura 27). Implementa DELIBERADAMENTE
/// tanto <see cref="DomainEvent"/> (para que <c>OutboxSaveChangesInterceptor</c> lo recolecte de
/// <see cref="Documento"/>) como <see cref="IIntegrationEvent"/> (para que <c>OutboxBatchProcessor</c> lo
/// publique), mismo patrón que <c>FeatureFlagActivadoIntegrationEvent</c> (Feature Management, Fase 6
/// módulo 4).
/// </summary>
public sealed record DocumentoSubidoIntegrationEvent(Guid DocumentoId, Guid VersionId, string NombreArchivo, string HashSha256)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Documents.DocumentoSubido";

    public int SchemaVersion => 1;

    /// <summary>Todos los eventos del mismo documento (subida inicial, nuevas versiones, escaneos) quedan
    /// en la misma partición -- un consumidor que necesite ver la historia de un documento en orden lo
    /// obtiene sin trabajo adicional.</summary>
    public string PartitionKey => DocumentoId.ToString();
}
