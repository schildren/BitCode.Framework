using BitCode.Framework.Platform.Notifications.Plantillas;

namespace BitCode.Framework.Platform.Notifications.Envio.Canales;

/// <summary>
/// Canal 100 % verificable sin infraestructura externa: "enviar" y "persistir" son la MISMA operación --
/// <c>NotificationSender</c> ya persiste la fila <see cref="Notification"/> (con
/// <see cref="Notification.Asunto"/>/<see cref="Notification.Cuerpo"/> ya renderizados) ANTES de invocar
/// cualquier <see cref="INotificationChannelSender"/>, así que para este canal no hay ningún efecto
/// adicional que ejecutar: el destinatario "recibe" la notificación en el sentido de que ya puede leerla
/// vía <c>GET /api/v1/notifications/notificaciones/{id}</c>. Siempre exitoso -- no existe ningún modo de
/// fallo transitorio/permanente propio de este canal (a diferencia de <see cref="EmailNotificationChannelSender"/>,
/// que depende de una red/servidor SMTP externo).
/// </summary>
internal sealed class InAppNotificationChannelSender : INotificationChannelSender
{
    public NotificationChannel Canal => NotificationChannel.InApp;

    public Task<NotificationSendResult> SendAsync(Notification notification, CancellationToken cancellationToken = default) =>
        Task.FromResult(NotificationSendResult.Exitoso());
}
