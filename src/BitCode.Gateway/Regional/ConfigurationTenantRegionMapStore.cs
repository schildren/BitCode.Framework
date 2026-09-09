using BitCode.Framework.Shared.Domain.MultiTenancy;
using Microsoft.Extensions.Options;

namespace BitCode.Gateway.Regional;

/// <summary>
/// Implementación de <see cref="ITenantRegionMapStore"/> (F5-02) respaldada por configuración
/// (<see cref="GatewayRegionalRoutingOptions.TenantRegionAssignments"/>, sección "Regional") en vez de
/// por el <c>InMemoryTenantRegionMapStore</c> de referencia de F5-02
/// (Shared.Infrastructure.Persistence): el Gateway es un proceso sin base de datos propia, así que la
/// fuente natural de la asignación tenant → región es la MISMA configuración externa (appsettings/
/// variables de entorno) que ya usa para todo lo demás (rutas YARP, rate limiting, límites de
/// tamaño) -- no un mapa que haya que poblar en runtime vía código.
/// </summary>
/// <remarks>
/// Se registra ANTES de <c>AddRegionalOwnership</c> (ver
/// <see cref="GatewayRegionalRoutingServiceCollectionExtensions"/>), que usa <c>TryAdd</c> para
/// <see cref="ITenantRegionMapStore"/>/<see cref="IRegionalOwnershipResolver"/> -- mismo mecanismo de
/// extensión documentado en <c>RegionalOwnershipServiceCollectionExtensions</c> (F5-02) para que un
/// consumidor con una implementación productiva propia no herede la implementación en memoria de
/// referencia. Esta implementación tampoco es la topología de replicación real entre regiones (F5-04):
/// sigue siendo, como el resto de F5-02/F5-03, un mecanismo de referencia/demostración -- la fuente de
/// verdad real de qué tenant vive en qué región es responsabilidad de la infraestructura/orquestador
/// que despliega y configura cada instancia regional del Gateway.
/// </remarks>
public sealed class ConfigurationTenantRegionMapStore(
    IOptionsMonitor<GatewayRegionalRoutingOptions> optionsMonitor)
    : ITenantRegionMapStore
{
    public Task<RegionId?> TryGetOwnerRegionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var assignments = optionsMonitor.CurrentValue.TenantRegionAssignments;

        if (assignments.TryGetValue(tenantId.ToString(), out var regionValue) &&
            !string.IsNullOrWhiteSpace(regionValue))
        {
            return Task.FromResult<RegionId?>(new RegionId(regionValue));
        }

        return Task.FromResult<RegionId?>(null);
    }
}
