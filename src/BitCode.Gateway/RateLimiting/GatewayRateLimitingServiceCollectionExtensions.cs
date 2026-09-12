using System.Threading.RateLimiting;
using BitCode.Framework.Shared.Infrastructure.Caching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BitCode.Gateway.RateLimiting;

/// <summary>
/// Rate limiting del Gateway (F4-08), política fixed window (nombre de política <see cref="PolicyName"/>)
/// leída de la sección de configuración "RateLimiting" (<see cref="GatewayRateLimitingOptions"/>),
/// aplicada explícitamente sobre el proxy YARP en Program.cs
/// (<c>.RequireRateLimiting(GatewayRateLimitingServiceCollectionExtensions.PolicyName)</c>).
/// </summary>
/// <remarks>
/// Cierre de pendiente F4-08: con <c>Caching:RedisConnectionString</c> configurado (misma clave y mismo
/// patrón "lazy, sin conectar en el registro de servicios" que ya usa
/// <c>SharedDataProtectionServiceCollectionExtensions</c> para el key ring de Data Protection, F4-03),
/// el contador de la ventana vive en Redis (<see cref="RedisFixedWindowRateLimiter"/>) -- compartido
/// entre TODAS las réplicas del Gateway, así que el límite configurado es el límite AGREGADO real, no
/// un límite por réplica multiplicado por la cantidad de réplicas. Sin Redis configurado, cae al rate
/// limiting nativo de ASP.NET Core en memoria (comportamiento previo a esta tarea, documentado como
/// riesgo conocido -- válido para una sola instancia o mientras el proyecto consumidor no configura
/// Redis).
/// </remarks>
public static class GatewayRateLimitingServiceCollectionExtensions
{
    public const string PolicyName = "gateway";

    private const string RedisKeyPrefix = "RateLimit";

    public static IServiceCollection AddGatewayRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GatewayRateLimitingOptions>(
            configuration.GetSection(GatewayRateLimitingOptions.SectionName));

        var gatewayOptions = configuration
            .GetSection(GatewayRateLimitingOptions.SectionName)
            .Get<GatewayRateLimitingOptions>() ?? new GatewayRateLimitingOptions();

        var redisConnectionString = configuration
            .GetSection(CachingOptions.SectionName)
            .Get<CachingOptions>()?.RedisConnectionString;

        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                AddInMemoryFixedWindowPolicy(rateLimiterOptions, gatewayOptions);
                return;
            }

            AddRedisFixedWindowPolicy(rateLimiterOptions, gatewayOptions, redisConnectionString, configuration);
        });

        return services;
    }

    /// <summary>
    /// Fallback documentado: sin <c>Caching:RedisConnectionString</c>, el rate limiting sigue siendo en
    /// memoria por réplica -- mismo comportamiento y misma limitación conocida que existía antes de esta
    /// tarea (con N réplicas, el límite efectivo es N * PermitLimit). Válido para una sola instancia
    /// (desarrollo/pruebas) o mientras el proyecto consumidor no configura Redis.
    /// </summary>
    private static void AddInMemoryFixedWindowPolicy(
        RateLimiterOptions rateLimiterOptions,
        GatewayRateLimitingOptions gatewayOptions)
    {
        rateLimiterOptions.AddFixedWindowLimiter(PolicyName, fixedWindowOptions =>
        {
            fixedWindowOptions.PermitLimit = gatewayOptions.PermitLimit;
            fixedWindowOptions.Window = TimeSpan.FromSeconds(gatewayOptions.WindowSeconds);
            fixedWindowOptions.QueueLimit = gatewayOptions.QueueLimit;
            fixedWindowOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        });
    }

    /// <summary>
    /// Contador compartido en Redis, atómico (<see cref="RedisFixedWindowRateLimiter"/>), entre TODAS
    /// las réplicas del Gateway que apunten al mismo Redis/prefijo de clave. Una única instancia del
    /// limiter para toda la política (partición constante "gateway-global") -- mismo comportamiento "un
    /// solo contador global" que <c>AddFixedWindowLimiter</c> sin partición explícita, la única
    /// diferencia es DÓNDE vive ese contador.
    /// </summary>
    private static void AddRedisFixedWindowPolicy(
        RateLimiterOptions rateLimiterOptions,
        GatewayRateLimitingOptions gatewayOptions,
        string redisConnectionString,
        IConfiguration configuration)
    {
        // Conexión propia (no la de AddStackExchangeRedisCache, que no expone su IConnectionMultiplexer
        // interno) -- mismo patrón "lazy, sin conectar en el registro de servicios" que
        // SharedDataProtectionServiceCollectionExtensions (F4-03): la conexión real ocurre en el primer
        // request, no bloquea el arranque del proceso.
        var lazyConnection = new Lazy<IConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(redisConnectionString));

        // Prefijo de clave calificado por servicio (OpenTelemetry:ServiceName, mismo identificador
        // lógico que ya usa el resto del framework, F1-27/F3-10) -- evita colisión de contadores si
        // varios servicios distintos comparten el mismo Redis.
        var serviceName = configuration["OpenTelemetry:ServiceName"];
        var redisKeyPrefix = string.IsNullOrWhiteSpace(serviceName)
            ? $"{RedisKeyPrefix}:{PolicyName}"
            : $"{RedisKeyPrefix}:{serviceName}:{PolicyName}";

        var limiter = new RedisFixedWindowRateLimiter(
            connectionFactory: () => lazyConnection.Value,
            redisKeyPrefix: redisKeyPrefix,
            permitLimit: gatewayOptions.PermitLimit,
            window: TimeSpan.FromSeconds(gatewayOptions.WindowSeconds));

        // Partición constante ("gateway-global"): PartitionedRateLimiter cachea la instancia devuelta
        // por la factory la primera vez que ve esa clave y la reutiliza en cada request siguiente --
        // efectivamente una única instancia de `limiter` para toda la política, igual que el fallback en
        // memoria (sin partición explícita por IP/ruta).
        rateLimiterOptions.AddPolicy(PolicyName, _ =>
            RateLimitPartition.Get("gateway-global", _ => limiter));
    }
}
