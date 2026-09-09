using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Regions;

/// <summary>
/// Implementación de referencia de <see cref="IRegionalOwnershipResolver"/> (F5-02): resuelve la
/// región propietaria de un tenant a partir de una asignación explícita en
/// <see cref="ITenantRegionMapStore"/>. Si el tenant no tiene una fila de asignación, resuelve a
/// <see cref="RegionId.Primary"/> — "sin asignación explícita" nunca significa "región indeterminada"
/// ni introduce aleatoriedad: significa, de forma determinística, "la región primaria es la
/// propietaria". Mismo patrón que <c>TenantShardMapResolver</c> (F1-13).
/// </summary>
/// <remarks>
/// Este resolver es <b>determinístico</b> siempre que <see cref="ITenantRegionMapStore"/> no
/// reasigne una fila existente concurrentemente con esta llamada: la resolución de un
/// <c>tenantId</c> dado depende únicamente del valor almacenado para ese <c>tenantId</c> en el
/// momento de la consulta, nunca del orden de resolución ni de cuántos otros tenants ya se
/// resolvieron. Esta propiedad es la base del criterio de aceptación de F5-02 ("sin escrituras
/// concurrentes ambiguas"): dos llamadas concurrentes para el mismo tenant, contra el mismo store,
/// siempre devuelven la misma región — nunca dos regiones distintas "compitiendo" por el mismo
/// tenant.
/// </remarks>
public sealed class TenantRegionOwnershipResolver(ITenantRegionMapStore regionMapStore) : IRegionalOwnershipResolver
{
    public async Task<RegionId> ResolveOwnerRegionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var explicitRegion = await regionMapStore.TryGetOwnerRegionAsync(tenantId, cancellationToken);
        return explicitRegion ?? RegionId.Primary;
    }
}
