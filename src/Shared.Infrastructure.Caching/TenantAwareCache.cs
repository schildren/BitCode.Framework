using BitCode.Framework.Shared.Domain.MultiTenancy;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BitCode.Framework.Shared.Infrastructure.Caching;

/// <summary>
/// Implementación por defecto de <see cref="ITenantAwareCache"/> (F1-16): antepone
/// <c>"tenant:{TenantId}:"</c> a la clave recibida antes de delegar en <see cref="HybridCache"/>. Se
/// apoya en <see cref="ITenantContext"/> (no <see cref="ITenantProvider"/> directamente, misma regla
/// que el resto del framework desde F1-15) porque ya viene memoizado e inmutable para el scope
/// actual, evitando resolver el tenant dos veces o exponerse a que cambie a mitad de una operación de
/// cache.
/// </summary>
/// <remarks>
/// <paramref name="logger"/> es opcional (por defecto <see cref="NullLogger{T}"/>) a propósito: es un
/// parámetro nuevo agregado en F5-06 sobre un tipo público ya con constructores existentes en código
/// consumidor y en pruebas (<c>new TenantAwareCache(cache, tenantContext)</c>) — hacerlo opcional
/// evita un breaking change de firma para quienes ya lo instancian directamente, y si se resuelve por
/// contenedor DI sin <c>ILogger&lt;TenantAwareCache&gt;</c> registrado (por ejemplo, un
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/> de prueba sin
/// <c>AddLogging()</c>), el propio contenedor usa este valor por defecto en vez de fallar la
/// resolución.
/// </remarks>
public sealed class TenantAwareCache(
    HybridCache cache,
    ITenantContext tenantContext,
    ILogger<TenantAwareCache>? logger = null) : ITenantAwareCache
{
    private readonly ILogger<TenantAwareCache> _logger = logger ?? NullLogger<TenantAwareCache>.Instance;


    public ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        // HybridCache ya trata una falla de L2 (Redis inalcanzable) como no fatal: cae de vuelta al
        // factory (la fuente de verdad) en vez de propagar la excepción -- verificado con Redis real
        // detenido en CacheUnavailabilityDoesNotBlockSourceOfTruthTests (F5-06,
        // docs/cache-regional-fase5.md sección 4). No se agrega ningún try/catch aquí: hacerlo
        // duplicaría una garantía que ya da HybridCache y podría ocultar un factory que sí debe fallar
        // (por ejemplo, si la fuente de verdad real no está disponible).
        cache.GetOrCreateAsync(BuildScopedKey(key), factory, options, tags, cancellationToken);

    /// <summary>
    /// Invalidación best-effort (F5-06, <c>docs/cache-regional-fase5.md</c> sección 3): a diferencia
    /// de <see cref="GetOrCreateAsync{T}"/>, <see cref="HybridCache.RemoveAsync(string, CancellationToken)"/>
    /// SÍ propaga la excepción de conectividad de L2 sin controlar (hallazgo real de
    /// <c>CacheUnavailabilityDoesNotBlockSourceOfTruthTests</c>, verificado con Redis real detenido).
    /// Como la invalidación entre regiones ya se documenta como best-effort (un evento de integración
    /// publicado tras el commit, con TTL corto como red de seguridad ante eventos perdidos — nunca la
    /// fuente de verdad de la consistencia), un fallo de L2 al intentar invalidar NUNCA debe propagar
    /// hacia el flujo de negocio que la disparó (p. ej. el handler que acaba de confirmar un commit):
    /// se registra como advertencia y se continúa. El TTL corto configurado en cada
    /// <see cref="HybridCacheEntryOptions"/> es quien garantiza, en el peor caso (evento de
    /// invalidación Y este intento de <c>RemoveAsync</c> ambos perdidos), que el dato stale deje de
    /// servirse igual, sin depender de que esta llamada tenga éxito.
    /// </summary>
    public async ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(BuildScopedKey(key), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "No se pudo invalidar la clave de cache '{Key}' (probable caída del cache regional/L2). " +
                "Se continúa sin propagar la falla -- la invalidación es best-effort y el TTL corto de " +
                "la entrada es la red de seguridad ante este caso.",
                key);
        }
    }

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
