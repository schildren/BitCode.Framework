using BitCode.Framework.Shared.Domain.MultiTenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Caching;

public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registra HybridCache (L1 en memoria, siempre activo). Si la sección "Caching" trae
    /// RedisConnectionString, además registra Redis como L2 compartido entre instancias —
    /// HybridCache lo detecta automáticamente vía el IDistributedCache ya registrado. Sin Redis
    /// configurado, un proyecto de una sola instancia sigue funcionando solo con la L1.
    /// </summary>
    /// <remarks>
    /// También registra <see cref="ITenantAwareCache"/> (F1-16, <c>TryAddScoped</c>) para que un
    /// proyecto multi-tenant tenga siempre disponible la variante de cache que compone el
    /// <c>TenantId</c> en la clave — necesita un <see cref="ITenantContext"/> ya registrado en el
    /// contenedor (lo registra <c>AddSharedPersistence</c>); si no lo está, la resolución de
    /// <see cref="ITenantAwareCache"/> falla recién al usarla, no en el arranque, igual que cualquier
    /// otra dependencia opcional del framework.
    /// </remarks>
    public static IServiceCollection AddSharedCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(CachingOptions.SectionName).Get<CachingOptions>() ?? new CachingOptions();

        if (!string.IsNullOrWhiteSpace(options.RedisConnectionString))
        {
            services.AddStackExchangeRedisCache(redis => redis.Configuration = options.RedisConnectionString);
        }

        services.AddHybridCache();
        services.TryAddScoped<ITenantAwareCache, TenantAwareCache>();

        return services;
    }
}
