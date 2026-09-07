namespace BitCode.Framework.Shared.Application.Eventing;

/// <summary>
/// Clasificación de un fallo de publicación/procesamiento de un <see cref="IIntegrationEvent"/>
/// (F3-07): distingue un error <b>transitorio</b> (reintentar tiene sentido — el broker está caído
/// momentáneamente, hubo un timeout de red) de uno <b>permanente</b> (reintentar nunca va a funcionar —
/// el payload no es serializable, el mensaje excede el tamaño máximo del broker, falta autorización).
/// </summary>
/// <remarks>
/// Esta clasificación acota el reintento (junto con <see cref="EventRetryPolicyOptions.MaxAttempts"/>,
/// que aplica igual a ambos casos como límite duro) — un error permanente no debería, en la práctica,
/// llegar nunca a agotar el límite máximo antes de marcarse como agotado, porque no tiene sentido
/// esperar el backoff completo para algo que ya sabemos que no va a funcionar. Aun así, F3-07
/// deliberadamente NO distingue el backoff aplicado entre transitorio/permanente (ambos usan
/// <see cref="EventRetryBackoff.CalculateDelay"/> igual) para mantener el mecanismo simple: la
/// clasificación importa sobre todo para decisiones futuras de F3-08 (DLQ) — por ejemplo, enviar un
/// error permanente a la cola de mensajes muertos de inmediato en el próximo ciclo, sin esperar a que
/// se agoten los <see cref="EventRetryPolicyOptions.MaxAttempts"/> intentos completos — que esta tarea
/// no implementa todavía.
/// </remarks>
public enum EventPublishFailureKind
{
    /// <summary>Reintentar puede tener éxito (broker temporalmente no disponible, timeout de red).</summary>
    Transient,

    /// <summary>Reintentar nunca va a funcionar (mensaje inválido, no autorizado, demasiado grande).</summary>
    Permanent,
}
