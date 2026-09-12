using BitCode.Framework.Shared.Infrastructure.Security.Oidc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.Bff;

/// <summary>
/// Registra el almacén de sesión server-side del BFF (F2-03) sobre <see cref="IDistributedCache"/>.
/// No mapea ningún endpoint HTTP ni depende de ASP.NET Core más allá de las abstracciones estándar de
/// caching -- eso lo hace <c>Shared.Infrastructure.Web.Security.Bff</c> (cookie de sesión, endpoints de
/// login/logout, proxy hacia las APIs protegidas), igual patrón que F2-02 (Security = protocolo/tokens,
/// Web = wiring HTTP).
/// </summary>
public static class BffSessionServiceCollectionExtensions
{
    /// <summary>
    /// Lee <see cref="BffSessionOptions"/> de la sección <c>"Bff:Session"</c> y registra
    /// <see cref="IBffSessionStore"/> (<see cref="DistributedCacheBffSessionStore"/>).
    /// </summary>
    /// <remarks>
    /// Requiere un <see cref="IDistributedCache"/> registrado para operar en modo distribuido -- si el
    /// proyecto consumidor ya llamó <c>AddSharedCaching</c> (Shared.Infrastructure.Caching) con
    /// <c>Caching:RedisConnectionString</c> configurado, la sesión del BFF automáticamente se comparte
    /// entre instancias sobre esa misma caché, sin introducir un mecanismo de sesión nuevo. Si ningún
    /// <see cref="IDistributedCache"/> fue registrado todavía (por ejemplo, un proyecto que solo usa el
    /// BFF y no llamó <c>AddSharedCaching</c>), este método agrega el fallback en memoria de proceso
    /// (memoria de proceso) -- válido únicamente para desarrollo/una sola instancia; un despliegue con
    /// más de una instancia del BFF necesita Redis/Valkey configurado para que las sesiones sobrevivan
    /// a un balanceo entre instancias.
    /// </remarks>
    public static IServiceCollection AddSharedBffSessionStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BffSessionOptions>(configuration.GetSection(BffSessionOptions.SectionName));

        // AddDistributedMemoryCache usa TryAdd internamente: si ya hay un IDistributedCache registrado
        // (p. ej. Redis vía AddSharedCaching), esta llamada no hace nada -- el fallback en memoria
        // nunca reemplaza a un backend distribuido ya configurado, sin importar el orden de llamada.
        services.AddDistributedMemoryCache();

        // El key ring se persiste en Redis cuando Caching:RedisConnectionString está configurado
        // (R-TEC-08, F4-03) -- ver SharedDataProtectionServiceCollectionExtensions para que todas las
        // réplicas del BFF compartan el mismo key ring y no queden dependientes de a qué pod caiga cada
        // request (lo que producía el efecto práctico de una sticky session no declarada).
        services.AddSharedDataProtectionWithSharedKeyRing(configuration);
        services.AddSingleton<IBffSessionStore, DistributedCacheBffSessionStore>();

        return services;
    }
}
