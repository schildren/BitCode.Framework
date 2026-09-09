namespace BitCode.Framework.Shared.Domain.MultiTenancy;

/// <summary>
/// Resuelve, de forma determinística, la región propietaria de escritura (single-writer) de un
/// tenant (F5-02, Fase 5 — Disaster Recovery y multi-región). Análogo a <see cref="IShardResolver"/>
/// (F1-13) en el eje de topología regional: dado el mismo <paramref name="tenantId"/> y el mismo
/// estado de <see cref="ITenantRegionMapStore"/>, siempre devuelve el mismo <see cref="RegionId"/>,
/// sin depender del orden de resolución, del número de tenants ya resueltos, ni de ningún otro estado
/// mutable. Esta propiedad es la que sustenta el criterio de aceptación de F5-02 ("sin escrituras
/// concurrentes ambiguas"): para cualquier tenant, en cualquier momento, existe un único
/// <see cref="RegionId"/> válido como propietario de escritura.
/// </summary>
public interface IRegionalOwnershipResolver
{
    /// <summary>
    /// Resuelve la región propietaria de escritura de <paramref name="tenantId"/>.
    /// </summary>
    Task<RegionId> ResolveOwnerRegionAsync(Guid tenantId, CancellationToken cancellationToken);
}
