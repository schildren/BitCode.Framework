namespace BitCode.Framework.Platform.Notifications.Envio.Canales;

public enum NotificationSendOutcome
{
    Exitoso,
    FalloTransitorio,
    FalloPermanente,
}

/// <summary>Resultado de un intento de <see cref="INotificationChannelSender.SendAsync"/> -- distingue
/// explícitamente fallo transitorio (reintentable, ver <see cref="Notification.RegistrarEnvioFallido"/>)
/// de fallo permanente (nunca se reintenta, mismo criterio que
/// <c>IEventPublishFailureClassifier</c>/<c>EventPublishFailureKind</c> de F3-07).</summary>
public sealed record NotificationSendResult(NotificationSendOutcome Outcome, string? ErrorMensaje = null)
{
    public static NotificationSendResult Exitoso() => new(NotificationSendOutcome.Exitoso);

    public static NotificationSendResult FalloTransitorio(string errorMensaje) =>
        new(NotificationSendOutcome.FalloTransitorio, errorMensaje);

    public static NotificationSendResult FalloPermanente(string errorMensaje) =>
        new(NotificationSendOutcome.FalloPermanente, errorMensaje);
}
