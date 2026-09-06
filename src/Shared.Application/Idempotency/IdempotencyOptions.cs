namespace BitCode.Framework.Shared.Application.Idempotency;

/// <summary>
/// Opciones de <c>AddSharedApplication</c> para <c>IdempotencyBehavior</c> (F1-22).
/// </summary>
public class IdempotencyOptions
{
    /// <summary>
    /// Cuánto tiempo se conserva el resultado guardado bajo una Idempotency-Key antes de tratarse
    /// como vencido (a partir de ahí, un reintento con esa misma clave se ejecuta como si fuera la
    /// primera vez). 24 horas por defecto — suficiente para cubrir reintentos de red o del cliente
    /// sin acumular filas indefinidamente; ver <c>IIdempotencyStore.PurgeExpiredAsync</c> para la
    /// limpieza física de las entradas ya vencidas.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(24);
}
