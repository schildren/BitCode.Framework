using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.IntegrationHub.Solicitudes;

/// <summary>Evento de integración público: una <see cref="IntegrationRequest"/> se envió con éxito al
/// conector externo. Registrado en <c>docs/catalogo-eventos.md</c> (regla dura 27). Sin consumidores
/// conocidos todavía -- un consumidor típico sería un módulo de auditoría/reporting externo (Fase 6,
/// módulos 11/12) que quiera reflejar el estado de las integraciones sin consultar este bounded context
/// directamente.</summary>
public sealed record SolicitudIntegracionEnviadaIntegrationEvent(Guid IntegrationRequestId, Guid ConnectorId, string ConnectorCodigo)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "IntegrationHub.SolicitudEnviada";

    public int SchemaVersion => 1;

    public string PartitionKey => IntegrationRequestId.ToString();
}
