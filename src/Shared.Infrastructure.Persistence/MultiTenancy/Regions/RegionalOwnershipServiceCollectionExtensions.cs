using BitCode.Framework.Shared.Domain.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Regions;

/// <summary>
/// Registro de servicios de ownership regional (F5-02, Fase 5 — Disaster Recovery y multi-región).
/// Deliberadamente <b>no</b> se agrega a <c>AddSharedPersistence</c> (F1-10): a diferencia de
/// sharding (F1-13/F1-14, que sí forma parte del ciclo de vida normal de persistencia multi-tenant),
/// el ownership regional es un concepto de topología de despliegue que un consumidor de un solo
/// host/región no necesita configurar en absoluto — mantenerlo en un método de extensión separado
/// evita forzar a todo consumidor existente de <c>AddSharedPersistence</c> a razonar sobre regiones.
/// </summary>
public static class RegionalOwnershipServiceCollectionExtensions
{
    /// <summary>
    /// Registra el mapa de ownership regional (F5-02) con la implementación de referencia en memoria
    /// (<see cref="InMemoryTenantRegionMapStore"/>/<see cref="TenantRegionOwnershipResolver"/>, ver
    /// sus remarks para las limitaciones de esa implementación) y fija la región de esta
    /// instancia/proceso en <paramref name="currentRegion"/>. Usa <c>TryAdd</c> para
    /// <see cref="ITenantRegionMapStore"/>/<see cref="IRegionalOwnershipResolver"/>: un consumidor que
    /// ya tenga una implementación productiva (p. ej. respaldada por una tabla de control replicada
    /// entre regiones) la registra ANTES de llamar a este método, igual que con
    /// <c>ITenantShardMapStore</c> (F1-13). <see cref="ICurrentRegionProvider"/> siempre se
    /// sobrescribe con el valor de <paramref name="currentRegion"/> — a diferencia de los dos
    /// anteriores, no tiene sentido que dos llamadas a este método en el mismo proceso dejen vigente
    /// una región "vieja".
    /// </summary>
    public static IServiceCollection AddRegionalOwnership(
        this IServiceCollection services,
        RegionId currentRegion)
    {
        services.TryAddSingleton<ITenantRegionMapStore, InMemoryTenantRegionMapStore>();
        services.TryAddSingleton<IRegionalOwnershipResolver, TenantRegionOwnershipResolver>();
        services.AddSingleton<ICurrentRegionProvider>(new StaticCurrentRegionProvider(currentRegion));

        return services;
    }
}
