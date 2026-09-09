using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.ImportExport.Importacion;

/// <summary>Evento de integración público: un <see cref="ImportJob"/> falló de forma DEFINITIVA a nivel de
/// job completo (por ejemplo, el archivo original ya no está disponible, o no hay un
/// <see cref="IImportRowHandler"/> registrado para su <c>TipoImportacion</c>) -- distinto de una fila
/// individual con error, que no hace fallar el job completo (ver el <c>remarks</c> de
/// <see cref="ImportJob"/>). Registrado en <c>docs/catalogo-eventos.md</c> (regla dura 27).</summary>
public sealed record ImportacionFallidaIntegrationEvent(Guid ImportJobId, string TipoImportacion, string ErrorMensaje)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "ImportExport.ImportacionFallida";

    public int SchemaVersion => 1;

    public string PartitionKey => ImportJobId.ToString();
}
