using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.ImportExport.Exportacion;

/// <summary>Evento de integración público: un <see cref="ExportJob"/> terminó de procesarse. Registrado en
/// <c>docs/catalogo-eventos.md</c> (regla dura 27). Sin consumidores conocidos todavía -- mismo criterio
/// que <c>ImportacionCompletadaIntegrationEvent</c>.</summary>
public sealed record ExportacionCompletadaIntegrationEvent(
    Guid ExportJobId, string TipoExportacion, int FilasExportadas, int FilasConError)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "ImportExport.ExportacionCompletada";

    public int SchemaVersion => 1;

    public string PartitionKey => ExportJobId.ToString();
}
