using BitCode.Framework.Shared.Application.Eventing;

namespace BitCode.Framework.Platform.Notifications;

/// <summary>
/// Opciones del módulo Notifications -- sección de configuración <c>"Notifications"</c>.
/// </summary>
/// <remarks>
/// <see cref="Retry"/> reutiliza <see cref="EventRetryPolicyOptions"/> (F3-07) TAL CUAL, sin
/// reimplementar backoff/reintentos -- mismo mecanismo que ya usa <c>OutboxPublisherOptions.Retry</c>
/// para el relay de eventos de integración. Un fallo TRANSITORIO de un canal (ver
/// <c>Envio.Canales.NotificationSendResult</c>) consume un intento de esta política; un fallo
/// PERMANENTE marca la notificación como fallida inmediatamente, sin consumir la política de
/// reintentos (ver <c>Notification.RegistrarEnvioFallidoPermanente</c>).
/// </remarks>
public sealed class NotificationsOptions
{
    public EventRetryPolicyOptions Retry { get; set; } = new();
}
