using BitCode.Framework.Shared.Domain.MultiTenancy;
using Microsoft.Extensions.Caching.Hybrid;

namespace BitCode.Framework.Shared.Infrastructure.Caching;

/// <summary>
/// Implementación por defecto de <see cref="ITenantAwareCache"/> (F1-16): antepone
/// <c>"tenant:{TenantId}:"</c> a la clave recibida antes de delegar en <see cref="HybridCache"/>. Se
/// apoya en <see cref="ITenantContext"/> (no <see cref="ITenantProvider"/> directamente, misma regla
/// que el resto del framework desde F1-15) porque ya viene memoizado e inmutable para el scope
/// actual, evitando resolver el tenant dos veces o exponerse a que cambie a mitad de una operación de
/// cache.
/// </summary>
public sealed class TenantAwareCache(HybridCache cache, ITenantContext tenantContext) : ITenantAwareCache
{
    public ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync(BuildScopedKey(key), factory, options, tags, cancellationToken);

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(BuildScopedKey(key), cancellationToken);

    /// <summary>
    /// Si el proyecto no opera en modo multi-tenant, la clave queda sin modificar (no hay ningún
    /// tenant del que distinguirla). Si opera en modo multi-tenant, exige un <c>TenantId</c> resuelto
    /// para el scope actual — nunca cachea con una clave "sin tenant" que un scope sin resolución
    /// completa (por ejemplo, un bug que deje pasar un request sin tenant) pudiera terminar
    /// compartiendo entre tenants por accidente.
    /// </summary>
    private string BuildScopedKey(string key)
    {
        if (!tenantContext.IsMultiTenancyEnabled)
        {
            return key;
        }

        if (tenantContext.TenantId is not { } tenantId)
        {
            throw new TenantResolutionException(
                "No se puede componer una clave de cache multi-tenant sin un TenantId resuelto para el scope actual.");
        }

        return $"tenant:{tenantId:D}:{key}";
    }
}
