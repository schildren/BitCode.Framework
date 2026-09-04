using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Caching;

public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registra HybridCache (L1 en memoria, siempre activo). Si la sección "Caching" trae
    /// RedisConnectionString, además registra Redis como L2 compartido entre instancias —
    /// HybridCache lo detecta automáticamente vía el IDistributedCache ya registrado. Sin Redis
    /// configurado, un proyecto de una sola instancia sigue funcionando solo con la L1.
    /// </summary>
    public static IServiceCollection AddSharedCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(CachingOptions.SectionName).Get<CachingOptions>() ?? new CachingOptions();

        if (!string.IsNullOrWhiteSpace(options.RedisConnectionString))
        {
            services.AddStackExchangeRedisCache(redis => redis.Configuration = options.RedisConnectionString);
        }

        services.AddHybridCache();

        return services;
    }
}
