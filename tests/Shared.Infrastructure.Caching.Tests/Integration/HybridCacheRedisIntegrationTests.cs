using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Caching.Tests.Integration;

/// <summary>
/// Cierra la Fase 4 verificando contra un Redis real (Testcontainers) el motivo de tener L2 en
/// primer lugar: que dos instancias de la aplicación (dos ServiceProvider/HybridCache
/// independientes, cada uno con su propia L1 vacía) comparten el valor cacheado a través de Redis,
/// no solo dentro del proceso.
/// </summary>
[Collection(RedisCollection.Name)]
public class HybridCacheRedisIntegrationTests(RedisContainerFixture fixture)
{
    private HybridCache BuildHybridCache()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Caching:RedisConnectionString"] = fixture.ConnectionString,
            })
            .Build();

        services.AddSharedCaching(configuration);
        var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<HybridCache>();
    }

    [Fact]
    public async Task ValueWrittenByOneInstance_IsVisibleToAnotherInstance_ViaRedisL2()
    {
        var cacheInstanceA = BuildHybridCache();
        var cacheInstanceB = BuildHybridCache();
        var key = $"shared-key-{Guid.NewGuid():N}";

        await cacheInstanceA.GetOrCreateAsync(key, _ => ValueTask.FromResult("valor-desde-instancia-A"));
        await Task.Delay(TimeSpan.FromSeconds(1));

        var factoryCalledOnInstanceB = false;
        var valueFromInstanceB = await cacheInstanceB.GetOrCreateAsync(key, _ =>
        {
            factoryCalledOnInstanceB = true;
            return ValueTask.FromResult("no-debería-usarse");
        });

        valueFromInstanceB.Should().Be("valor-desde-instancia-A");
        factoryCalledOnInstanceB.Should().BeFalse("el valor debió llegar desde Redis (L2), no recalcularse");
    }
}
