namespace BitCode.Framework.Platform.Notifications.Envio;

public enum NotificationEstado
{
    PendienteDeEnvio = 0,
    Enviada = 1,
    OmitidaPorPreferencia = 2,
    PendienteDeReintento = 3,

    /// <summary>Agotó <c>EventRetryPolicyOptions.MaxAttempts</c> (F3-07, reutilizado -- ver
    /// <c>docs/guia-notifications.md</c>, sección "Retry") sin lograr una entrega exitosa.</summary>
    Fallida = 4,
}
