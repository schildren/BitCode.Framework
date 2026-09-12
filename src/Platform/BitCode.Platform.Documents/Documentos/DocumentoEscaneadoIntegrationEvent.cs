using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Documents.Documentos;

/// <summary>
/// Evento de integración público del módulo Documents -- el hecho de negocio "una versión terminó de
/// escanearse" (Épica de Documents: "Escaneo antivirus"), con el resultado (<see cref="Resultado"/>). Un
/// consumidor real puede reaccionar a <see cref="EstadoEscaneo.Infectado"/> disparando una alerta de
/// seguridad, sin sondear el estado de la versión. Registrado en <c>docs/catalogo-eventos.md</c> (regla
/// dura 27).
/// </summary>
public sealed record DocumentoEscaneadoIntegrationEvent(Guid DocumentoId, Guid VersionId, int Numero, EstadoEscaneo Resultado)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Documents.DocumentoEscaneado";

    public int SchemaVersion => 1;

    public string PartitionKey => DocumentoId.ToString();
}
