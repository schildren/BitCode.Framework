using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Sharding;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Sharding;

/// <summary>
/// F1-13: el resolver por defecto debe preservar exactamente el comportamiento de T1 (F1-12) — todo
/// tenant, exista o no, resuelve siempre al mismo shard compartido.
/// </summary>
public class SharedDatabaseShardResolverTests
{
    [Fact]
    public async Task ResolveShardAsync_AnyTenant_AlwaysResolvesToSharedShard()
    {
        var resolver = new SharedDatabaseShardResolver();

        var shardA = await resolver.ResolveShardAsync(Guid.NewGuid(), CancellationToken.None);
        var shardB = await resolver.ResolveShardAsync(Guid.NewGuid(), CancellationToken.None);
        var shardEmpty = await resolver.ResolveShardAsync(Guid.Empty, CancellationToken.None);

        shardA.Should().Be(ShardId.Shared);
        shardB.Should().Be(ShardId.Shared);
        shardEmpty.Should().Be(ShardId.Shared);
    }
}
