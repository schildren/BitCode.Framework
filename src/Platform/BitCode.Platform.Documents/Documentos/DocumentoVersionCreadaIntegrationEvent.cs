using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Documents.Documentos;

/// <summary>
/// Evento de integración público del módulo Documents -- el hecho de negocio "se cargó una nueva versión
/// de un documento existente" (número mayor a 1). Ver <see cref="DocumentoSubidoIntegrationEvent"/> para
/// el caso simétrico de la primera versión. Registrado en <c>docs/catalogo-eventos.md</c> (regla dura 27).
/// </summary>
public sealed record DocumentoVersionCreadaIntegrationEvent(Guid DocumentoId, Guid VersionId, int Numero, string HashSha256)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Documents.DocumentoVersionCreada";

    public int SchemaVersion => 1;

    public string PartitionKey => DocumentoId.ToString();
}
