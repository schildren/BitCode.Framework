using Microsoft.Extensions.Caching.Hybrid;

namespace BitCode.Framework.Shared.Infrastructure.Caching;

/// <summary>
/// F1-16: envoltorio de <see cref="HybridCache"/> que compone el <c>TenantId</c> del scope actual
/// (vía <see cref="BitCode.Framework.Shared.Domain.MultiTenancy.ITenantContext"/>) dentro de la clave
/// de cache. Un proyecto multi-tenant real que cachee datos sensibles a tenant (por ejemplo, el
/// resultado de una query de negocio) debe usar esta interfaz — nunca <see cref="HybridCache"/>
/// directamente con una clave "lógica" sin el tenant — porque <see cref="HybridCache"/> no tiene
/// ningún concepto de tenant propio: dos tenants que pidan el mismo recurso lógico (por ejemplo,
/// "producto con slug X") con la misma clave literal recibirían el mismo valor cacheado si la clave
/// no distingue el tenant, lo que sería una fuga de información entre tenants (ver
/// <c>docs/threat-model.md</c>, hallazgo I5, y <c>docs/adr/0006-cache-hybridcache-valkey-redis.md</c>).
/// </summary>
/// <remarks>
/// <see cref="HybridCache"/> directamente sigue siendo la opción correcta para cachear datos que NO
/// son sensibles a tenant (metadata global, configuración compartida entre todos los tenants de un
/// mismo despliegue) — en ese caso, componer el <c>TenantId</c> en la clave sería incorrecto (todos
/// los tenants deben compartir la misma entrada cacheada).
/// </remarks>
public interface ITenantAwareCache
{
    /// <summary>
    /// Igual semántica que <see cref="HybridCache.GetOrCreateAsync{T}(string, Func{CancellationToken, ValueTask{T}}, HybridCacheEntryOptions?, IEnumerable{string}?, CancellationToken)"/>,
    /// pero la clave efectiva usada contra <see cref="HybridCache"/> incluye el <c>TenantId</c>
    /// resuelto para el scope actual. Si el proyecto opera en modo multi-tenant
    /// (<c>ITenantContext.IsMultiTenancyEnabled == true</c>) y no hay ningún <c>TenantId</c> resuelto
    /// para el scope actual, lanza <see cref="BitCode.Framework.Shared.Domain.MultiTenancy.TenantResolutionException"/>
    /// en vez de cachear con una clave ambigua — mismo principio de "fallar de forma segura" que
    /// <c>HttpContextTenantProvider</c> (F1-12).
    /// </summary>
    ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina la entrada cacheada para <paramref name="key"/> dentro del scope del tenant actual
    /// (misma composición de clave que <see cref="GetOrCreateAsync{T}"/>) — nunca elimina la entrada
    /// de otro tenant que comparta la misma clave lógica.
    /// </summary>
    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);
}
