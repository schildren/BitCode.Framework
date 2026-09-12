using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.ImportExport.Importacion;

/// <summary>Evento de integración público: un <see cref="ImportJob"/> terminó de procesarse (con o sin
/// filas con error -- <paramref name="FilasConError"/> distingue ambos casos, ver
/// <see cref="ImportJobEstado.CompletadoConErrores"/>). Registrado en <c>docs/catalogo-eventos.md</c>
/// (regla dura 27). Sin consumidores conocidos todavía -- un consumidor típico sería una notificación al
/// usuario que inició la importación (integrando con Notifications, Fase 6 módulo 8) o un reporte de
/// auditoría externo.</summary>
public sealed record ImportacionCompletadaIntegrationEvent(
    Guid ImportJobId, string TipoImportacion, int FilasTotales, int FilasProcesadas, int FilasConError)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "ImportExport.ImportacionCompletada";

    public int SchemaVersion => 1;

    public string PartitionKey => ImportJobId.ToString();
}
