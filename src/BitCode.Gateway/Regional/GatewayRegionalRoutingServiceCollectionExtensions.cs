using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Regions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Gateway.Regional;

/// <summary>
/// Registro del routing regional del Gateway (F5-03, Fase 5 — Disaster Recovery y multi-región).
/// Reutiliza el mecanismo de ownership de F5-02 (<see cref="IRegionalOwnershipResolver"/>/
/// <see cref="ICurrentRegionProvider"/>, <c>AddRegionalOwnership</c> de
/// Shared.Infrastructure.Persistence) en vez de duplicarlo: el Gateway solo aporta (a) la fuente de la
/// asignación tenant → región vía configuración (<see cref="ConfigurationTenantRegionMapStore"/>, en
/// vez del <c>InMemoryTenantRegionMapStore</c> de referencia) y (b) el middleware que compara, por
/// request, la región propietaria del tenant contra la región de esta instancia
/// (<see cref="RegionalOwnershipRoutingMiddleware"/>).
/// </summary>
public static class GatewayRegionalRoutingServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayRegionalRouting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GatewayRegionalRoutingOptions>(
            configuration.GetSection(GatewayRegionalRoutingOptions.SectionName));

        // Registrado ANTES de AddRegionalOwnership (TryAddSingleton más abajo) para que gane esta
        // implementación respaldada por configuración -- ver remarks de ConfigurationTenantRegionMapStore.
        services.TryAddSingleton<ITenantRegionMapStore, ConfigurationTenantRegionMapStore>();

        var currentRegionValue = configuration
            .GetSection(GatewayRegionalRoutingOptions.SectionName)[nameof(GatewayRegionalRoutingOptions.CurrentRegion)];
        var currentRegion = string.IsNullOrWhiteSpace(currentRegionValue)
            ? RegionId.Primary
            : new RegionId(currentRegionValue);

        // AddRegionalOwnership (F5-02) agrega IRegionalOwnershipResolver (TenantRegionOwnershipResolver,
        // vía TryAdd -- no pisa nada) y fija ICurrentRegionProvider a currentRegion.
        services.AddRegionalOwnership(currentRegion);

        return services;
    }
}
