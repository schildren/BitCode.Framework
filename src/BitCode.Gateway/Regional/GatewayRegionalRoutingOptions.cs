namespace BitCode.Gateway.Regional;

/// <summary>
/// Routing regional del Gateway (F5-03, Fase 5 — Disaster Recovery y multi-región). Configura, para
/// ESTA instancia del Gateway, en qué región corre (<see cref="CurrentRegion"/>) y, de forma opcional,
/// el mapa explícito tenant → región propietaria (<see cref="TenantRegionAssignments"/>) que reemplaza
/// al <c>InMemoryTenantRegionMapStore</c> de referencia de F5-02 (que no admite seed por configuración)
/// -- ver <see cref="ConfigurationTenantRegionMapStore"/>.
/// </summary>
/// <remarks>
/// Defaults deliberados para "cero cambio de comportamiento" en un despliegue de una sola
/// región/host (el único escenario real hoy en este repositorio, ver <c>docs/bia-fase5.md</c>):
/// <see cref="CurrentRegion"/> es <c>"primary"</c> (igual que <c>RegionId.Primary</c>) y
/// <see cref="TenantRegionAssignments"/> está vacío (ningún tenant tiene asignación explícita, todos
/// resuelven a la región primaria) -- con estos defaults, <see cref="RegionalOwnershipRoutingMiddleware"/>
/// nunca rechaza ningún request.
/// </remarks>
public sealed class GatewayRegionalRoutingOptions
{
    public const string SectionName = "Regional";

    /// <summary>Región de cómputo en la que corre esta instancia del Gateway.</summary>
    public string CurrentRegion { get; set; } = "primary";

    /// <summary>
    /// Asignación explícita tenant (clave, <see cref="Guid"/> en formato string) → región propietaria
    /// de escritura (valor). Un tenant sin entrada acá resuelve a <c>RegionId.Primary</c> -- mismo
    /// criterio determinístico que <c>TenantRegionOwnershipResolver</c> (F5-02,
    /// Shared.Infrastructure.Persistence): "sin asignación explícita" nunca significa "indeterminado".
    /// </summary>
    public Dictionary<string, string> TenantRegionAssignments { get; set; } = new();
}
