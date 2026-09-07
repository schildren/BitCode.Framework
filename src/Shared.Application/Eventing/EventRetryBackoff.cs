namespace BitCode.Framework.Shared.Application.Eventing;

/// <summary>
/// Cálculo puro (sin efectos secundarios, sin bloquear ningún hilo) de backoff exponencial con "full
/// jitter" (F3-07) — mismo patrón estándar (base × 2^intento, con tope, aleatorizado uniformemente
/// entre 0 y ese tope) documentado por AWS
/// ("Exponential Backoff And Jitter") y ya aplicado del lado HTTP saliente del framework
/// (<c>HttpResilienceOptions</c>/Polly, F1-26) — ver el <c>remarks</c> de
/// <see cref="EventRetryPolicyOptions"/> para por qué esta tarea no reutiliza Polly directamente para
/// este cálculo.
/// </summary>
public static class EventRetryBackoff
{
    /// <summary>
    /// Calcula cuánto esperar antes del próximo intento número <paramref name="attemptNumber"/> (1 =
    /// primer reintento, después del intento original que ya falló).
    /// </summary>
    /// <param name="attemptNumber">
    /// Número de reintento (1-based) para el que se calcula la demora — típicamente
    /// <c>OutboxMessage.RetryCount</c> después de incrementarlo por el fallo actual. Valores menores a 1
    /// se tratan como 1.
    /// </param>
    /// <param name="options">Política de reintentos (base, tope, máximo de intentos).</param>
    /// <param name="random">
    /// Fuente de aleatoriedad para el jitter; por defecto <see cref="Random.Shared"/>. Parametrizable
    /// solo para pruebas deterministas (por ejemplo, verificar el límite superior con una semilla fija).
    /// </param>
    public static TimeSpan CalculateDelay(int attemptNumber, EventRetryPolicyOptions options, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.BaseDelay <= TimeSpan.Zero)
        {
            // Backoff deshabilitado explícitamente (típicamente en pruebas): reintento inmediato.
            return TimeSpan.Zero;
        }

        var effectiveAttempt = Math.Max(1, attemptNumber);
        random ??= Random.Shared;

        // 2^(intento - 1) acotado a 30 para no desbordar double en intentos muy altos (30 ya excede
        // ampliamente cualquier MaxDelay razonable, así que el resultado ya está topeado igual).
        var exponent = Math.Min(effectiveAttempt - 1, 30);
        var exponentialMs = options.BaseDelay.TotalMilliseconds * Math.Pow(2, exponent);
        var cappedMs = Math.Min(exponentialMs, options.MaxDelay.TotalMilliseconds);

        // "Full jitter": uniforme entre 0 y el tope exponencial — evita que múltiples filas/instancias
        // que fallaron al mismo tiempo (por ejemplo, todas por el mismo broker caído) vuelvan a
        // reintentar exactamente en el mismo instante ("thundering herd"), igual que el jitter
        // obligatorio de la pipeline HTTP (F1-26).
        var jitteredMs = random.NextDouble() * cappedMs;

        return TimeSpan.FromMilliseconds(jitteredMs);
    }

    /// <summary>
    /// Indica si, luego de <paramref name="attemptsSoFar"/> intentos ya realizados (incluido el que
    /// acaba de fallar), no corresponde reintentar más — el mensaje debe marcarse como agotado en vez
    /// de seguir reintentando indefinidamente.
    /// </summary>
    public static bool IsExhausted(int attemptsSoFar, EventRetryPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return attemptsSoFar >= options.MaxAttempts;
    }
}
