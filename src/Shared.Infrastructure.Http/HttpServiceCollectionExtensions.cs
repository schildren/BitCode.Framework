using BitCode.Framework.Shared.Infrastructure.Http.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace BitCode.Framework.Shared.Infrastructure.Http;

/// <summary>
/// Registro de <see cref="System.Net.Http.HttpClient"/> salientes con la pipeline de resiliencia
/// estándar del framework (F1-26): timeout por intento + total, retry con backoff exponencial y
/// jitter, circuit breaker y un limitador de concurrencia (bulkhead). Construida sobre
/// <c>Microsoft.Extensions.Http.Resilience</c> (Polly v8), el mecanismo estándar de .NET para
/// <see cref="System.Net.Http.IHttpClientFactory"/> — no reimplementa ningún patrón de resiliencia a
/// mano.
/// </summary>
/// <remarks>
/// Esta tarea prepara el mecanismo reutilizable para cuando un módulo futuro (Fase 2+) necesite
/// llamar a un servicio HTTP externo; hoy ningún consumidor real del framework declara un
/// <see cref="System.Net.Http.HttpClient"/> saliente propio. No debe confundirse con la resiliencia
/// de operaciones de base de datos (F1-10, <c>PersistenceOptions.CommandTimeoutSeconds</c>) ni con
/// rate limiting del lado servidor (fuera de alcance de esta tarea): el foco es exclusivamente
/// llamadas HTTP salientes desde este proceso hacia un servicio externo.
/// </remarks>
public static class HttpServiceCollectionExtensions
{
    /// <summary>
    /// Registra <typeparamref name="TClient"/> como cliente HTTP tipado (<c>AddHttpClient&lt;TClient&gt;</c>)
    /// con la pipeline de resiliencia estándar del framework, usando los valores por defecto de
    /// <see cref="HttpResilienceOptions"/>.
    /// </summary>
    public static IHttpClientBuilder AddResilientHttpClient<TClient>(this IServiceCollection services)
        where TClient : class =>
        services.AddResilientHttpClient<TClient>(static _ => { });

    /// <summary>
    /// Registra <typeparamref name="TClient"/> como cliente HTTP tipado con la pipeline de
    /// resiliencia estándar del framework, configurando <see cref="HttpResilienceOptions"/>
    /// explícitamente en código (por ejemplo, para ajustar timeouts o el máximo de reintentos de una
    /// integración puntual).
    /// </summary>
    public static IHttpClientBuilder AddResilientHttpClient<TClient>(
        this IServiceCollection services,
        Action<HttpResilienceOptions> configureOptions)
        where TClient : class
    {
        var options = new HttpResilienceOptions();
        configureOptions(options);

        var httpClientBuilder = services.AddHttpClient<TClient>();
        httpClientBuilder.AddStandardResilienceHandler(pipeline => ApplyOptions(pipeline, options));

        return httpClientBuilder;
    }

    /// <summary>
    /// Registra <typeparamref name="TClient"/> como cliente HTTP tipado con la pipeline de
    /// resiliencia estándar del framework, enlazando <see cref="HttpResilienceOptions"/> desde
    /// <paramref name="configuration"/> (sección <paramref name="sectionName"/>, o
    /// <c>"HttpResilience:{NombreDeTClient}"</c> si no se especifica).
    /// </summary>
    public static IHttpClientBuilder AddResilientHttpClient<TClient>(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sectionName = null)
        where TClient : class
    {
        var options = new HttpResilienceOptions();
        var section = configuration.GetSection(sectionName ?? $"{HttpResilienceOptions.DefaultSectionName}:{typeof(TClient).Name}");
        section.Bind(options);

        var httpClientBuilder = services.AddHttpClient<TClient>();
        httpClientBuilder.AddStandardResilienceHandler(pipeline => ApplyOptions(pipeline, options));

        return httpClientBuilder;
    }

    /// <summary>
    /// Traduce <see cref="HttpResilienceOptions"/> (superficie configurable del framework) a
    /// <see cref="HttpStandardResilienceOptions"/> (superficie completa de Polly/Microsoft.Extensions.Http.Resilience),
    /// fijando siempre el predicado de retry seguro de <see cref="HttpRetrySafety"/> — esa pieza no es
    /// configurable desde <see cref="HttpResilienceOptions"/> a propósito (F1-26, criterio de
    /// aceptación "Retry solo en operaciones seguras").
    /// </summary>
    private static void ApplyOptions(HttpStandardResilienceOptions pipeline, HttpResilienceOptions options)
    {
        pipeline.Retry.MaxRetryAttempts = options.RetryMaxAttempts;
        pipeline.Retry.Delay = options.RetryBaseDelay;
        pipeline.Retry.BackoffType = DelayBackoffType.Exponential;
        pipeline.Retry.UseJitter = true;
        pipeline.Retry.ShouldHandle = HttpRetrySafety.CreateShouldHandlePredicate();

        pipeline.AttemptTimeout.Timeout = options.AttemptTimeout;
        pipeline.TotalRequestTimeout.Timeout = options.TotalTimeout;

        pipeline.CircuitBreaker.FailureRatio = options.CircuitBreakerFailureRatio;
        pipeline.CircuitBreaker.MinimumThroughput = options.CircuitBreakerMinimumThroughput;
        pipeline.CircuitBreaker.SamplingDuration = options.CircuitBreakerSamplingDuration;
        pipeline.CircuitBreaker.BreakDuration = options.CircuitBreakerBreakDuration;

        pipeline.RateLimiter.DefaultRateLimiterOptions.PermitLimit = options.MaxConcurrentRequests;
        pipeline.RateLimiter.DefaultRateLimiterOptions.QueueLimit = options.ConcurrencyQueueLimit;
    }
}
