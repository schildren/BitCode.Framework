using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Application.Regions;

/// <summary>Errores propios de <c>RegionalOwnershipBehavior</c> (F5-02).</summary>
public static class RegionalOwnershipErrors
{
    /// <summary>
    /// Un comando <c>IRegionalCommand</c> se ejecutó en una instancia cuya región (
    /// <c>ICurrentRegionProvider.CurrentRegion</c>) no coincide con la región propietaria de
    /// escritura del tenant actual (<c>IRegionalOwnershipResolver.ResolveOwnerRegionAsync</c>).
    /// Decisión F5-02: se rechaza en vez de ejecutarse igual — permitirlo introduciría exactamente el
    /// escenario que la decisión arquitectónica rectora de multi-región prohíbe (más de un escritor
    /// válido para el mismo agregado/bounded context al mismo tiempo).
    /// </summary>
    public static Error WrongRegion(Guid tenantId, string ownerRegion, string currentRegion) => Error.Conflict(
        "RegionalOwnership.WrongRegion",
        $"El tenant {tenantId} tiene a '{ownerRegion}' como región propietaria de escritura; esta instancia " +
        $"corre en '{currentRegion}' y no puede procesar este comando. Reintentar contra la región propietaria.");
}
