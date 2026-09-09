using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Notifications.Envio;

/// <summary>Evento de integración público: una <see cref="Notification"/> se entregó con éxito.
/// Registrado en <c>docs/catalogo-eventos.md</c> (regla dura 27). Sin consumidores conocidos todavía --
/// un consumidor típico sería un módulo de auditoría/reporting externo que quiera reflejar el historial
/// de notificaciones sin consultar este bounded context directamente.</summary>
public sealed record NotificacionEnviadaIntegrationEvent(
    Guid NotificationId, Guid DestinatarioUserId, string CodigoPlantilla, NotificationChannel Canal)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Notifications.NotificacionEnviada";

    public int SchemaVersion => 1;

    public string PartitionKey => NotificationId.ToString();
}
