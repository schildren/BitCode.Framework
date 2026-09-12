namespace BitCode.Framework.Platform.Notifications.Plantillas;

/// <summary>
/// Canal de entrega de una notificación (Fase 6, módulo 8: "canales" del Plan Maestro). Ver
/// <c>Envio/Canales/*NotificationChannelSender.cs</c> para el nivel de "realismo" de cada implementación
/// de referencia -- ninguna es un adapter productivo hacia un proveedor comercial (SendGrid, Twilio,
/// FCM/APNs, etc.), ver <c>docs/guia-notifications.md</c>, sección "Canales".
/// </summary>
public enum NotificationChannel
{
    /// <summary>Correo electrónico real vía SMTP (<c>System.Net.Mail.SmtpClient</c>) -- funciona contra
    /// cualquier servidor SMTP real (incluido uno de prueba, ver
    /// <c>Sample.Notifications.Api.Tests</c>), pero requiere que el llamador provea la dirección de
    /// contacto del destinatario explícitamente (este módulo no resuelve el email de un
    /// <c>UserId</c> -- no tiene acceso a Identity Administration, ver
    /// <c>docs/guia-notifications.md</c>).</summary>
    Email = 0,

    /// <summary>Notificación interna persistida para que el propio destinatario la lea vía
    /// <c>GET /api/v1/notifications/notificaciones/{id}</c> -- el canal 100 % verificable sin
    /// infraestructura externa: "enviar" y "persistir" son la misma operación.</summary>
    InApp = 1,
}
