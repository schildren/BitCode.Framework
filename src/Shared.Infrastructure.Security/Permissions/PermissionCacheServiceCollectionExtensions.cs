using BitCode.Framework.Shared.Infrastructure.Caching;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Registra el cache de permisos (F2-09): decora el <see cref="IPermissionService"/> ya registrado
/// (por <c>AddSharedSecurity</c>/<c>AddSharedOidcAuthentication</c>/<c>AddSharedPermissionEvaluation</c>)
/// con <see cref="CachedPermissionService"/>, sin tocar <see cref="IPermissionEvaluator"/> ni
/// <see cref="PermissionEvaluator"/> -- ambos siguen funcionando exactamente igual, ahora sobre un
/// <see cref="IPermissionService"/> que evita SQL Server en el camino "hit". Ver <c>docs/guia-rbac-2.md</c>
/// para el comportamiento de cache (TTL, invalidación) y por qué la decoración ocurre en esta capa y no en
/// el evaluador.
/// </summary>
public static class PermissionCacheServiceCollectionExtensions
{
    /// <summary>
    /// Debe llamarse DESPUÉS de <c>AddSharedSecurity</c>/<c>AddSharedOidcAuthentication</c> (o, como
    /// mínimo, de <c>AddSharedPermissionEvaluation</c>) -- necesita que <see cref="IPermissionService"/>
    /// ya esté registrado para poder decorarlo. Lanza <see cref="InvalidOperationException"/> en el
    /// arranque (nunca en tiempo de request) si no encuentra ningún registro previo, para que el error
    /// de orden de llamadas sea evidente de inmediato.
    /// </summary>
    /// <remarks>
    /// Llamarla ANTES de <c>AddSharedSecurity</c> (sobre el <see cref="NullPermissionService"/> que deja
    /// <c>AddSharedPermissionEvaluation</c>) NO lanza excepción acá -- es indistinguible del caso legítimo
    /// de un proyecto solo-OIDC que decide cachear (como no-op inofensivo) un
    /// <see cref="NullPermissionService"/> que nunca va a ser reemplazado. En cambio, si ese proyecto SÍ
    /// llama a <c>AddSharedSecurity</c> después -- es decir, el orden real es
    /// <c>AddSharedPermissionEvaluation</c> → <c>AddSharedPermissionCache</c> →
    /// <c>AddSharedSecurity</c> -- el <see cref="InvalidOperationException"/> se lanza en ese momento
    /// desde <c>AddSharedSecurity</c> (ver su documentación), porque ahí es donde el framework puede
    /// distinguir con certeza el orden inválido: la implementación real de RBAC está llegando después de
    /// que el decorador de cache ya se fijó sobre el <see cref="NullPermissionService"/>, y el
    /// <c>AddScoped</c> posterior de <c>AddSharedSecurity</c> dejaría el decorador huérfano (sin
    /// excepción, sin cache, silenciosamente) si no se detectara ahí.
    /// </remarks>
    public static IServiceCollection AddSharedPermissionCache(
        this IServiceCollection services,
        Action<PermissionCacheOptions>? configureOptions = null)
    {
        // Idempotente -- mismo criterio que AddSharedPermissionEvaluation/AddSharedAbacAuthorization: una
        // segunda llamada no debe volver a decorar un IPermissionService que ya es un
        // CachedPermissionService (evitaría un doble envoltorio inocuo pero desperdiciado). Ver
        // IsPermissionCacheAlreadyApplied -- la misma comprobación la usa AddSharedSecurity para detectar
        // el orden de llamada inválido documentado más abajo.
        if (services.IsPermissionCacheAlreadyApplied())
        {
            return services;
        }

        var innerDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IPermissionService))
            ?? throw new InvalidOperationException(
                "AddSharedPermissionCache debe llamarse después de registrar IPermissionService " +
                "(AddSharedSecurity, AddSharedOidcAuthentication o AddSharedPermissionEvaluation).");

        services.AddOptions<PermissionCacheOptions>();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        // Mismo fallback que AddSharedPermissionEvaluation (F2-07) -- ITenantAwareCache necesita
        // ITenantContext, y un proyecto que solo llamó a AddSharedOidcAuthentication (sin
        // AddSharedPersistence) podría no tenerlo registrado todavía.
        services.TryAddScoped<ITenantProvider, NullTenantProvider>();
        services.TryAddScoped<ITenantContext, TenantContext>();

        // AddHybridCache()/TryAddScoped<ITenantAwareCache> son ambos idempotentes (el primero registra
        // servicios internos con TryAdd; el segundo es TryAdd explícito) -- seguro de llamar aunque el
        // proyecto ya haya llamado AddSharedCaching() antes por su cuenta.
        services.AddHybridCache();
        services.TryAddScoped<ITenantAwareCache, TenantAwareCache>();

        services.TryAddScoped(sp => new CachedPermissionService(
            (IPermissionService)ResolveInner(sp, innerDescriptor),
            sp.GetRequiredService<ITenantAwareCache>(),
            sp.GetRequiredService<HybridCache>(),
            sp.GetRequiredService<IOptions<PermissionCacheOptions>>()));

        // Replace (no TryAdd): reemplaza el registro "crudo" de IPermissionService (PermissionService<,>
        // o NullPermissionService) por el decorador -- el registro original sigue resolvible construyendo
        // manualmente la instancia interna (ResolveInner), nunca a través del contenedor (evita
        // recursión infinita contra el propio decorador).
        services.Replace(ServiceDescriptor.Describe(
            typeof(IPermissionService),
            sp => sp.GetRequiredService<CachedPermissionService>(),
            innerDescriptor.Lifetime));

        services.Replace(ServiceDescriptor.Describe(
            typeof(IPermissionCacheInvalidator),
            sp => sp.GetRequiredService<CachedPermissionService>(),
            innerDescriptor.Lifetime));

        return services;
    }

    /// <summary>
    /// Construye la instancia "interna" (no cacheada) del <see cref="IPermissionService"/> ya registrado
    /// antes de esta llamada, replicando el mismo mecanismo de resolución que usaría el contenedor
    /// (instancia ya construida, factory, o tipo de implementación) -- sin pasar por
    /// <c>sp.GetRequiredService&lt;IPermissionService&gt;()</c>, que a esta altura ya resolvería el propio
    /// decorador.
    /// </summary>
    private static object ResolveInner(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(sp);
        }

        return ActivatorUtilities.GetServiceOrCreateInstance(sp, descriptor.ImplementationType!);
    }
}
