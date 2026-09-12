using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Sharding;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Sharding;

/// <summary>
/// F1-13 (estrategia T2, prototipo): pruebas de determinismo del mapeo explícito tenant → shard.
/// </summary>
public class TenantShardMapResolverTests
{
    [Fact]
    public async Task ResolveShardAsync_TenantWithoutExplicitAssignment_ResolvesToSharedShard()
    {
        var store = new InMemoryTenantShardMapStore();
        var resolver = new TenantShardMapResolver(store);
        var tenantId = Guid.NewGuid();

        var shard = await resolver.ResolveShardAsync(tenantId, CancellationToken.None);

        shard.Should().Be(ShardId.Shared);
    }

    [Fact]
    public async Task ResolveShardAsync_SameTenantResolvedRepeatedly_AlwaysReturnsSameShard()
    {
        var store = new InMemoryTenantShardMapStore();
        var tenantId = Guid.NewGuid();
        var expectedShard = new ShardId("shard-03");
        store.Assign(tenantId, expectedShard);
        var resolver = new TenantShardMapResolver(store);

        var results = new List<ShardId>();
        for (var i = 0; i < 20; i++)
        {
            results.Add(await resolver.ResolveShardAsync(tenantId, CancellationToken.None));
        }

        results.Should().AllBeEquivalentTo(expectedShard);
    }

    [Fact]
    public async Task ResolveShardAsync_DifferentTenantsWithExplicitAssignments_DistributeAcrossShards()
    {
        var store = new InMemoryTenantShardMapStore();
        var resolver = new TenantShardMapResolver(store);
        var tenantsPerShard = new Dictionary<ShardId, List<Guid>>
        {
            [new ShardId("shard-01")] = [Guid.NewGuid(), Guid.NewGuid()],
            [new ShardId("shard-02")] = [Guid.NewGuid(), Guid.NewGuid()],
            [new ShardId("shard-03")] = [Guid.NewGuid(), Guid.NewGuid()],
        };
        foreach (var (shardId, tenantIds) in tenantsPerShard)
        {
            foreach (var tenantId in tenantIds)
            {
                store.Assign(tenantId, shardId);
            }
        }

        foreach (var (expectedShardId, tenantIds) in tenantsPerShard)
        {
            foreach (var tenantId in tenantIds)
            {
                var resolvedShard = await resolver.ResolveShardAsync(tenantId, CancellationToken.None);
                resolvedShard.Should().Be(expectedShardId);
            }
        }
    }

    [Fact]
    public async Task ResolveShardAsync_AssigningNewTenant_DoesNotRelocateExistingTenants()
    {
        var store = new InMemoryTenantShardMapStore();
        var resolver = new TenantShardMapResolver(store);
        var existingTenantId = Guid.NewGuid();
        var existingShard = new ShardId("shard-01");
        store.Assign(existingTenantId, existingShard);

        var shardBeforeNewTenant = await resolver.ResolveShardAsync(existingTenantId, CancellationToken.None);

        // Agregar un tenant nuevo a un shard distinto no debe reubicar al tenant existente: a
        // diferencia de un hash consistente módulo N (donde agregar un shard recalcula la asignación
        // de todos los tenants existentes), un mapeo explícito por fila es independiente por tenant.
        var newTenantId = Guid.NewGuid();
        store.Assign(newTenantId, new ShardId("shard-02"));

        var shardAfterNewTenant = await resolver.ResolveShardAsync(existingTenantId, CancellationToken.None);

        shardBeforeNewTenant.Should().Be(existingShard);
        shardAfterNewTenant.Should().Be(existingShard);
    }
}
