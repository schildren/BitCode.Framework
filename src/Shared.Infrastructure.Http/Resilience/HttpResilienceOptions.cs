namespace BitCode.Framework.Shared.Infrastructure.Http.Resilience;

/// <summary>
/// Opciones configurables de la pipeline de resiliencia HTTP saliente estándar del framework
/// (F1-26): timeout (por intento y total), retry con backoff exponencial y jitter, circuit breaker
/// y un limitador de concurrencia (bulkhead) por cliente. Ver
/// <see cref="HttpServiceCollectionExtensions.AddResilientHttpClient{TClient}(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{HttpResilienceOptions})"/>.
/// </summary>
/// <remarks>
/// Los valores por defecto son razonables para un servicio HTTP externo genérico, pero cada
/// integración real (Fase 2+) debe revisarlos contra el SLA/comportamiento documentado del servicio
/// que consume — no hay un valor universalmente correcto de <see cref="RetryMaxAttempts"/> o
/// <see cref="CircuitBreakerBreakDuration"/>. Esta clase NO expone ningún control para deshabilitar
/// la condición de "solo reintentar operaciones seguras" (ver <see cref="HttpRetrySafety"/>): esa es
/// una garantía dura del framework, no un parámetro de afinación.
/// </remarks>
public sealed class HttpResilienceOptions
{
    /// <summary>
    /// Sección de configuración por defecto para la sobrecarga de
    /// <see cref="HttpServiceCollectionExtensions.AddResilientHttpClient{TClient}(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration, string?)"/>
    /// que enlaza desde <c>appsettings</c>. La sección efectiva por defecto es
    /// <c>"HttpResilience:{NombreDeTClient}"</c> para que dos clientes tipados distintos puedan tener
    /// políticas independientes sin colisionar.
    /// </summary>
    public const string DefaultSectionName = "HttpResilience";

    /// <summary>Cantidad máxima de reintentos (sin contar el intento original). Default: 3.</summary>
    public int RetryMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Demora base del backoff exponencial con jitter entre reintentos. Default: 1 segundo. El
    /// jitter (aleatoriedad agregada por Polly sobre esta base) es siempre obligatorio — evita que
    /// múltiples instancias del mismo servicio reintenten al mismo tiempo contra la dependencia caída
    /// ("thundering herd").
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Timeout aplicado a cada intento individual (incluye reintentos). Default: 10 segundos.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout total de la operación, incluidos todos los reintentos. Default: 30 segundos. Debe ser
    /// mayor que <see cref="AttemptTimeout"/> más el tiempo esperado de los reintentos, o la pipeline
    /// nunca llega a agotar <see cref="RetryMaxAttempts"/>.
    /// </summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Proporción de fallos (0-1) dentro de <see cref="CircuitBreakerSamplingDuration"/> que abre el
    /// circuito. Default: 0.1 (10 %).
    /// </summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.1;

    /// <summary>
    /// Cantidad mínima de llamadas dentro de <see cref="CircuitBreakerSamplingDuration"/> antes de que
    /// el circuit breaker pueda evaluar <see cref="CircuitBreakerFailureRatio"/>. Default: 10.
    /// </summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Ventana deslizante sobre la que se calcula <see cref="CircuitBreakerFailureRatio"/>. Default:
    /// 30 segundos. Debe ser al menos el doble de <see cref="AttemptTimeout"/> (requisito de Polly);
    /// si se reduce <see cref="AttemptTimeout"/> también puede ser necesario reducir este valor.
    /// </summary>
    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Tiempo que el circuito permanece abierto (fallando rápido, sin intentar la llamada real) antes
    /// de pasar a semiabierto. Default: 5 segundos.
    /// </summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Cantidad máxima de llamadas concurrentes permitidas hacia este cliente (bulkhead). Default:
    /// 100. Protege recursos propios (hilos, sockets) de saturarse por llamadas paralelas ilimitadas
    /// hacia un mismo servicio externo.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 100;

    /// <summary>
    /// Cantidad de llamadas que pueden esperar en cola una vez alcanzado
    /// <see cref="MaxConcurrentRequests"/> antes de rechazarse. Default: 0 (rechazo inmediato, sin
    /// cola) — preferido sobre encolar indefinidamente, que solo trasladaría la saturación a la
    /// latencia percibida por el llamador.
    /// </summary>
    public int ConcurrencyQueueLimit { get; set; }
}
