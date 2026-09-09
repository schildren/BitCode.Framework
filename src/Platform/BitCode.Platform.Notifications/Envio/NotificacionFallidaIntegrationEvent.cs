using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Notifications.Envio;

/// <summary>Evento de integración público: una <see cref="Notification"/> agotó sus reintentos
/// (F3-07, ver <c>docs/guia-notifications.md</c>, sección "Retry") sin lograr una entrega exitosa.
/// Registrado en <c>docs/catalogo-eventos.md</c> (regla dura 27). Sin consumidores conocidos todavía --
/// un consumidor típico sería un canal de alerta operacional (por ejemplo, un dashboard de Fase 6 módulo
/// 12) que quiera visibilizar notificaciones fallidas sin consultar este bounded context
/// directamente.</summary>
public sealed record NotificacionFallidaIntegrationEvent(
    Guid NotificationId, Guid DestinatarioUserId, string CodigoPlantilla, NotificationChannel Canal, string UltimoErrorMensaje)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Notifications.NotificacionFallida";

    public int SchemaVersion => 1;

    public string PartitionKey => NotificationId.ToString();
}
