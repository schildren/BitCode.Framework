using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.IntegrationHub.Solicitudes;

/// <summary>Evento de integración público: una <see cref="IntegrationRequest"/> agotó sus reintentos (o
/// falló de forma permanente) sin poder entregarse al conector externo. Registrado en
/// <c>docs/catalogo-eventos.md</c> (regla dura 27) -- un consumidor típico sería un canal de alerta
/// operacional (por ejemplo, un dashboard de Fase 6, módulo 12).</summary>
public sealed record SolicitudIntegracionFallidaIntegrationEvent(
    Guid IntegrationRequestId, Guid ConnectorId, string ConnectorCodigo, string UltimoErrorMensaje)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "IntegrationHub.SolicitudFallida";

    public int SchemaVersion => 1;

    public string PartitionKey => IntegrationRequestId.ToString();
}
