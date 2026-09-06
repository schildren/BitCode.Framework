using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BitCode.Framework.Shared.Infrastructure.Caching.HealthChecks;

/// <summary>
/// Health check de READINESS (F1-25) para Redis: hace un `GetAsync` trivial (nunca escribe) contra
/// el `IDistributedCache` registrado por `AddStackExchangeRedisCache` (ver
/// `CachingServiceCollectionExtensions.AddSharedCaching`). Solo se registra cuando el proyecto
/// consumidor configuró `Caching:RedisConnectionString` -- sin Redis configurado, `IDistributedCache`
/// no está registrado en el contenedor y este check tampoco se agrega (ver
/// `docs/guia-health-checks.md`).
/// </summary>
internal sealed class RedisDistributedCacheHealthCheck(IDistributedCache cache) : IHealthCheck
{
    private const string ProbeKey = "__bitcode-health-check-probe__";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.GetAsync(ProbeKey, cancellationToken);
            return HealthCheckResult.Healthy("Redis disponible.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Redis no responde.", ex);
        }
    }
}
