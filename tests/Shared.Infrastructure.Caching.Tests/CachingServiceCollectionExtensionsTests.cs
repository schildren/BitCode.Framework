using BitCode.Framework.Shared.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Caching.Tests;

public class CachingServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddSharedCaching_WithoutRedis_WorksWithInProcessL1Only()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSharedCaching(configuration);

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();

        var value = await cache.GetOrCreateAsync(
            "test-key",
            _ => ValueTask.FromResult("valor-cacheado"));

        value.Should().Be("valor-cacheado");
    }

    [Fact]
    public async Task AddSharedCaching_GetOrCreate_ReturnsCachedValueOnSecondCall()
    {
        var services = new ServiceCollection();
        services.AddSharedCaching(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();

        var callCount = 0;
        Task<string> Factory()
        {
            callCount++;
            return Task.FromResult("valor");
        }

        await cache.GetOrCreateAsync("counter-key", _ => new ValueTask<string>(Factory()));
        await cache.GetOrCreateAsync("counter-key", _ => new ValueTask<string>(Factory()));

        callCount.Should().Be(1, "la segunda llamada debe resolverse desde caché sin invocar la factory");
    }
}
