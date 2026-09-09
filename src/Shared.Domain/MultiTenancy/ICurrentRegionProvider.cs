namespace BitCode.Framework.Shared.Domain.MultiTenancy;

/// <summary>
/// Identifica la región de cómputo en la que se ejecuta <b>esta</b> instancia/proceso (F5-02, Fase 5
/// — Disaster Recovery y multi-región). No depende del tenant ni del request actual: es una propiedad
/// de despliegue de la instancia (análoga a "en qué datacenter/zona corre este pod"), configurada una
/// vez al arrancar el proceso — típicamente desde configuración/variables de entorno del orquestador,
/// nunca resuelta dinámicamente por request.
/// </summary>
/// <remarks>
/// <see cref="IRegionalOwnershipResolver"/> responde "¿qué región DEBERÍA atender la escritura de
/// este tenant?"; <see cref="ICurrentRegionProvider"/> responde "¿en qué región estoy corriendo YO,
/// ahora mismo?". Comparar ambos valores es lo que permite a un consumidor (ver
/// <c>RegionalOwnershipBehavior</c>, Shared.Application) detectar y rechazar de forma determinística
/// un intento de escritura fuera de la región propietaria del tenant.
/// </remarks>
public interface ICurrentRegionProvider
{
    /// <summary>Región de cómputo en la que corre esta instancia/proceso.</summary>
    RegionId CurrentRegion { get; }
}
