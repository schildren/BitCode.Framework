using BitCode.Framework.Shared.Infrastructure.Caching;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc;

/// <summary>
/// Registro compartido de <see cref="IDataProtectionProvider"/> para
/// <c>OidcAuthorizationCodeServiceCollectionExtensions.AddSharedOidcAuthorizationCodeFlow</c> (F2-02) y
/// <c>BffSessionServiceCollectionExtensions.AddSharedBffSessionStore</c> (F2-03). Ambos módulos usan el
/// mismo proveedor (<c>AddDataProtection()</c> es idempotente vía <c>TryAdd</c>) y se distinguen entre sí
/// con un "Purpose" propio (<see cref="AuthorizationCode.OidcAuthorizationCodeStateProtector"/> vs
/// <see cref="Bff.DistributedCacheBffSessionStore"/>), nunca con un key ring distinto.
/// </summary>
/// <remarks>
/// R-TEC-08 (F4-03): sin persistir el key ring en un backend compartido entre réplicas, cada pod cifra
/// con su propia clave por defecto (perfil de usuario del proceso / <c>%HOME%/.aspnet/DataProtection-Keys</c>
/// sin volumen persistente en contenedores) -- rompe "pod reemplazable" para cualquier consumidor que
/// despliegue BFF/Authorization Code con más de una réplica sin balanceo con afinidad. Esta clase
/// persiste el key ring en Redis (mismo backend ya aprobado para caching -- <c>AddSharedCaching</c>,
/// <c>Caching:RedisConnectionString</c>) cuando está configurado, evitando introducir un backend nuevo o
/// una dependencia de terceros no evaluada; el paquete
/// <c>Microsoft.AspNetCore.DataProtection.StackExchangeRedis</c> es first-party de ASP.NET Core (misma
/// licencia MIT y mismo ciclo de versionado que el resto de los paquetes <c>Microsoft.AspNetCore.*</c> ya
/// referenciados por este proyecto), por lo que no dispara el proceso de excepción de dependencias de
/// terceros de la sección 3.2 del Plan Maestro.
/// </remarks>
internal static class SharedDataProtectionServiceCollectionExtensions
{
    /// <summary>
    /// Nombre de aplicación por defecto del key ring compartido cuando el proyecto consumidor no
    /// configuró <c>OpenTelemetry:ServiceName</c> (F1-27/F3-10, ya usado en el framework como
    /// identificador lógico del servicio). Es un valor fijo -- no varía entre pods de un mismo
    /// despliegue -- para que todas las réplicas de un mismo proceso lógico compartan el mismo key ring
    /// sin necesidad de configuración adicional. Distinguirlo de otras aplicaciones que compartan el
    /// mismo Redis es responsabilidad de configurar <c>OpenTelemetry:ServiceName</c>.
    /// </summary>
    private const string DefaultKeyRingApplicationName = "bitcode-framework-oidc-bff";

    private const string RedisKeyPrefix = "DataProtection-Keys";

    /// <summary>
    /// Registra <see cref="IDataProtectionProvider"/> con un <c>ApplicationName</c> estable y, si
    /// <c>Caching:RedisConnectionString</c> está configurado, persiste el key ring en Redis
    /// (<c>PersistKeysToStackExchangeRedis</c>) para que todas las réplicas del mismo proceso lo
    /// compartan. Sin Redis configurado, cae al almacenamiento por defecto de Data Protection --
    /// válido únicamente para desarrollo/una sola instancia (ver R-TEC-08 en
    /// <c>docs/risk-register.md</c>).
    /// </summary>
    public static IServiceCollection AddSharedDataProtectionWithSharedKeyRing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var applicationName = configuration["OpenTelemetry:ServiceName"];
        var dataProtectionBuilder = services.AddDataProtection()
            .SetApplicationName(string.IsNullOrWhiteSpace(applicationName) ? DefaultKeyRingApplicationName : applicationName);

        var redisConnectionString = configuration.GetSection(CachingOptions.SectionName).Get<CachingOptions>()?.RedisConnectionString;
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            return services;
        }

        // Conexión propia (no la de AddStackExchangeRedisCache, que no expone su IConnectionMultiplexer
        // interno) -- mismo patrón "lazy, sin conectar en el registro de servicios" que
        // AddStackExchangeRedisCache usa para IDistributedCache: la conexión real ocurre en el primer
        // uso, no bloquea el arranque del proceso.
        var lazyConnection = new Lazy<IConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(redisConnectionString));
        dataProtectionBuilder.PersistKeysToStackExchangeRedis(() => lazyConnection.Value.GetDatabase(), RedisKeyPrefix);

        return services;
    }
}
