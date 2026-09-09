using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.Notifications.Envio;

/// <summary>Todas las notificaciones RECIBIDAS por el actor autenticado -- SIEMPRE acotada al propio
/// actor, mismo criterio de seguridad que <c>BandejaDeActorSpecification</c> de Task Inbox.</summary>
internal sealed class NotificacionesDeDestinatarioSpecification : Specification<Notification>
{
    public NotificacionesDeDestinatarioSpecification(Guid destinatarioUserId, NotificationEstado? estado)
    {
        ApplyCriteria(n => n.DestinatarioUserId == destinatarioUserId && (estado == null || n.Estado == estado));
        ApplyOrderByDescending(n => n.CreatedAtUtc);
    }
}
