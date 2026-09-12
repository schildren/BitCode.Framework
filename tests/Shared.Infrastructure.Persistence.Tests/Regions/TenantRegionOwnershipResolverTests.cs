using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Regions;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Regions;

/// <summary>
/// F5-02 (ownership regional): pruebas de determinismo del mapeo explícito tenant → región
/// propietaria de escritura. Mismo enfoque que <c>TenantShardMapResolverTests</c> (F1-13), aplicado al
/// eje de topología regional en vez del eje de partición de base de datos.
/// </summary>
public class TenantRegionOwnershipResolverTests
{
    [Fact]
    public async Task ResolveOwnerRegionAsync_TenantWithoutExplicitAssignment_ResolvesToPrimaryRegion()
    {
        var store = new InMemoryTenantRegionMapStore();
        var resolver = new TenantRegionOwnershipResolver(store);
        var tenantId = Guid.NewGuid();

        var region = await resolver.ResolveOwnerRegionAsync(tenantId, CancellationToken.None);

        region.Should().Be(RegionId.Primary);
    }

    [Fact]
    public async Task ResolveOwnerRegionAsync_SameTenantResolvedRepeatedly_AlwaysReturnsSameRegion()
    {
        var store = new InMemoryTenantRegionMapStore();
        var tenantId = Guid.NewGuid();
        var expectedRegion = new RegionId("eu-west");
        store.Assign(tenantId, expectedRegion);
        var resolver = new TenantRegionOwnershipResolver(store);

        var results = new List<RegionId>();
        for (var i = 0; i < 20; i++)
        {
            results.Add(await resolver.ResolveOwnerRegionAsync(tenantId, CancellationToken.None));
        }

        results.Should().AllBeEquivalentTo(expectedRegion);
    }

    [Fact]
    public async Task ResolveOwnerRegionAsync_ConcurrentCallsFromSimulatedRegions_AgreeOnSameOwner()
    {
        // Simula dos instancias/"regiones" (eu-west y us-east) consultando concurrentemente el mismo
        // store compartido para el mismo tenant — ninguna de las dos debe ver una región distinta:
        // esta es la propiedad que sustenta el criterio de aceptación de F5-02 ("sin escrituras
        // concurrentes ambiguas").
        var store = new InMemoryTenantRegionMapStore();
        var tenantId = Guid.NewGuid();
        var expectedRegion = new RegionId("us-east");
        store.Assign(tenantId, expectedRegion);
        var resolverInEuWest = new TenantRegionOwnershipResolver(store);
        var resolverInUsEast = new TenantRegionOwnershipResolver(store);

        var resolutions = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => i % 2 == 0
            ? resolverInEuWest.ResolveOwnerRegionAsync(tenantId, CancellationToken.None)
            : resolverInUsEast.ResolveOwnerRegionAsync(tenantId, CancellationToken.None)));

        resolutions.Should().AllBeEquivalentTo(expectedRegion,
            "dos resolvers concurrentes sobre el mismo store nunca deben devolver regiones distintas " +
            "para el mismo tenant");
    }

    [Fact]
    public async Task ResolveOwnerRegionAsync_DifferentTenantsWithExplicitAssignments_EachHasSingleOwner()
    {
        var store = new InMemoryTenantRegionMapStore();
        var resolver = new TenantRegionOwnershipResolver(store);
        var tenantsPerRegion = new Dictionary<RegionId, List<Guid>>
        {
            [new RegionId("eu-west")] = [Guid.NewGuid(), Guid.NewGuid()],
            [new RegionId("us-east")] = [Guid.NewGuid(), Guid.NewGuid()],
        };
        foreach (var (regionId, tenantIds) in tenantsPerRegion)
        {
            foreach (var tenantId in tenantIds)
            {
                store.Assign(tenantId, regionId);
            }
        }

        foreach (var (expectedRegionId, tenantIds) in tenantsPerRegion)
        {
            foreach (var tenantId in tenantIds)
            {
                var resolvedRegion = await resolver.ResolveOwnerRegionAsync(tenantId, CancellationToken.None);
                resolvedRegion.Should().Be(expectedRegionId);
            }
        }
    }

    [Fact]
    public async Task ResolveOwnerRegionAsync_AssigningNewTenant_DoesNotRelocateExistingTenants()
    {
        var store = new InMemoryTenantRegionMapStore();
        var resolver = new TenantRegionOwnershipResolver(store);
        var existingTenantId = Guid.NewGuid();
        var existingRegion = new RegionId("eu-west");
        store.Assign(existingTenantId, existingRegion);

        var regionBeforeNewTenant = await resolver.ResolveOwnerRegionAsync(existingTenantId, CancellationToken.None);

        var newTenantId = Guid.NewGuid();
        store.Assign(newTenantId, new RegionId("us-east"));

        var regionAfterNewTenant = await resolver.ResolveOwnerRegionAsync(existingTenantId, CancellationToken.None);

        regionBeforeNewTenant.Should().Be(existingRegion);
        regionAfterNewTenant.Should().Be(existingRegion);
    }
}
