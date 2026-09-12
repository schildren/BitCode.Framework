using BitCode.Framework.Platform.Notifications.Plantillas;

namespace BitCode.Framework.Platform.Notifications.Envio.Canales;

/// <summary>
/// Adapter de entrega de UN <see cref="NotificationChannel"/> concreto (Fase 6, módulo 8: "canales" del
/// Plan Maestro) -- al menos dos implementaciones de referencia registradas por defecto
/// (<c>EmailNotificationChannelSender</c>, <c>InAppNotificationChannelSender</c>), ver el
/// <c>remarks</c> de cada una para el nivel de "realismo" concreto. <c>NotificationSender</c> resuelve
/// la implementación correspondiente a <see cref="Notification.Canal"/> por <see cref="Canal"/>, nunca
/// por el tipo .NET concreto.
/// </summary>
public interface INotificationChannelSender
{
    NotificationChannel Canal { get; }

    Task<NotificationSendResult> SendAsync(Notification notification, CancellationToken cancellationToken = default);
}
