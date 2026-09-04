using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Caching.Tests.Integration;

[Collection(RedisCollection.Name)]
public class DistributedCacheDiagnosticTests(RedisContainerFixture fixture)
{
    [Fact]
    public async Task RawStackExchangeRedisCache_WriteThenReadFromDifferentProvider_Works()
    {
        var servicesA = new ServiceCollection();
        servicesA.AddStackExchangeRedisCache(redis => redis.Configuration = fixture.ConnectionString);
        using var providerA = servicesA.BuildServiceProvider();
        var cacheA = providerA.GetRequiredService<IDistributedCache>();

        var key = $"diag-{Guid.NewGuid():N}";
        await cacheA.SetAsync(key, Encoding.UTF8.GetBytes("hola"), new DistributedCacheEntryOptions());

        var servicesB = new ServiceCollection();
        servicesB.AddStackExchangeRedisCache(redis => redis.Configuration = fixture.ConnectionString);
        using var providerB = servicesB.BuildServiceProvider();
        var cacheB = providerB.GetRequiredService<IDistributedCache>();

        var bytes = await cacheB.GetAsync(key);

        bytes.Should().NotBeNull();
        Encoding.UTF8.GetString(bytes!).Should().Be("hola");
    }
}
