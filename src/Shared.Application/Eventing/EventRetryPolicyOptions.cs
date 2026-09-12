namespace BitCode.Framework.Shared.Application.Eventing;

/// <summary>
/// Política de reintentos con backoff exponencial y jitter para publicación/procesamiento de eventos de
/// integración (F3-07): cuántos intentos totales tolerar antes de considerar un mensaje "agotado" y
/// cómo espaciar los reintentos intermedios. Reutilizada tanto por el relay de Outbox
/// (<c>OutboxPublisherOptions.Retry</c>, <c>Shared.Infrastructure.Persistence</c>) como, en la medida
/// que el diseño actual del consumidor lo permite, por <c>KafkaEventConsumer&lt;TEvent&gt;</c>
/// (<c>Shared.Infrastructure.Messaging.Kafka</c>) — mismo criterio de "una única política de
/// resiliencia reutilizable" que <c>HttpResilienceOptions</c> (F1-26) aplica al lado HTTP saliente.
/// </summary>
/// <remarks>
/// No se implementó reutilizando directamente <c>Microsoft.Extensions.Http.Resilience</c>/Polly (F1-26):
/// esa pipeline está diseñada para envolver la ejecución de un delegado dentro de UNA operación activa
/// (reintentar la misma llamada varias veces antes de devolver el control al llamador). El backoff de
/// F3-07 necesita lo opuesto — calcular CUÁNDO debe ocurrir el PRÓXIMO ciclo de sondeo (una marca de
/// tiempo futura persistida en <c>OutboxMessage.LockedUntilUtc</c>), no bloquear el hilo actual
/// esperando un <c>Task.Delay</c> dentro de la misma invocación. Agregar una dependencia nueva
/// (`Polly.Core` directo) solo para una fórmula de backoff con jitter habría sido una dependencia
/// desproporcionada para el problema (ver <c>docs/politica-dependencias.md</c>, "no introducir una
/// dependencia sin revisar... compatibilidad" con el resto del diseño) — <see cref="EventRetryBackoff"/>
/// implementa el mismo patrón (backoff exponencial + jitter completo) sin esa dependencia.
/// </remarks>
public sealed class EventRetryPolicyOptions
{
    /// <summary>
    /// Cantidad máxima de intentos TOTALES (incluye el primero) antes de que un mensaje se considere
    /// agotado. Default: 10. Debe ser mayor a 0.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// Demora base del backoff exponencial. Con <see cref="System.TimeSpan.Zero"/>, el reintento ocurre
    /// sin demora (útil en pruebas). Default: 2 segundos.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Tope superior del backoff exponencial, antes de aplicar el jitter. Default: 5 minutos.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);
}
