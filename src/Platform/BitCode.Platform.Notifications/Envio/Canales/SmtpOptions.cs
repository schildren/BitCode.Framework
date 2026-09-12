namespace BitCode.Framework.Platform.Notifications.Envio.Canales;

/// <summary>Opciones de <see cref="EmailNotificationChannelSender"/> -- sección de configuración
/// <c>"Notifications:Smtp"</c>.</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 25;

    public bool EnableSsl { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }

    /// <summary>Dirección "From" de todos los emails que envía este canal -- un remitente fijo por
    /// despliegue, no configurable por notificación individual en este primer corte.</summary>
    public string FromAddress { get; set; } = "no-reply@bitcode.local";
}
