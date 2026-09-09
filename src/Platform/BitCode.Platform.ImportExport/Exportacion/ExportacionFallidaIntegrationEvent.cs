using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.ImportExport.Exportacion;

/// <summary>Evento de integración público: un <see cref="ExportJob"/> falló de forma DEFINITIVA a nivel de
/// job completo (por ejemplo, no hay un <see cref="IExportDataSource"/> registrado para su
/// <c>TipoExportacion</c>, o la fuente de datos lanzó una excepción no controlada al pedir una página).
/// Registrado en <c>docs/catalogo-eventos.md</c> (regla dura 27).</summary>
public sealed record ExportacionFallidaIntegrationEvent(Guid ExportJobId, string TipoExportacion, string ErrorMensaje)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "ImportExport.ExportacionFallida";

    public int SchemaVersion => 1;

    public string PartitionKey => ExportJobId.ToString();
}
