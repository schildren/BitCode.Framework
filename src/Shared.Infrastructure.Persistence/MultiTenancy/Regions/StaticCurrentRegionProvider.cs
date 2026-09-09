using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Regions;

/// <summary>
/// Implementación de referencia de <see cref="ICurrentRegionProvider"/> (F5-02): la región de esta
/// instancia/proceso es fija durante todo su ciclo de vida, configurada una única vez al componer los
/// servicios (típicamente desde configuración/variable de entorno del orquestador — p. ej.
/// <c>REGION_ID</c> inyectada por el manifiesto de despliegue de cada región). No hay ningún miembro
/// de escritura porque la región de una instancia en ejecución no cambia — moverla a otra región es,
/// por definición, desplegar una instancia nueva, no reconfigurar la existente.
/// </summary>
public sealed class StaticCurrentRegionProvider(RegionId currentRegion) : ICurrentRegionProvider
{
    public RegionId CurrentRegion { get; } = currentRegion;
}
