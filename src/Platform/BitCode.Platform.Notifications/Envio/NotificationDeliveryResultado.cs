namespace BitCode.Framework.Platform.Notifications.Envio;

/// <summary>Resultado de UN intento de entrega, registrado en <see cref="NotificationDelivery"/> --
/// tracking (Fase 6, módulo 8: "tracking" del Plan Maestro).</summary>
public enum NotificationDeliveryResultado
{
    Exitoso = 0,
    FalloTransitorio = 1,
    FalloPermanente = 2,

    /// <summary>El intento nunca llegó a invocar el canal -- la preferencia del destinatario lo
    /// descartó antes (ver <see cref="Notification.MarcarOmitidaPorPreferencia"/>).</summary>
    OmitidoPorPreferencia = 3,
}
