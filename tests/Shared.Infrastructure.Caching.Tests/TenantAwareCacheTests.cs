using BitCode.Framework.Shared.Domain.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Caching.Tests;

/// <summary>
/// F1-16 (Épica F1-C, pruebas de aislamiento): cierra el vector de contaminación de cache entre
/// tenants que ninguna tarea anterior había probado. <c>HybridCache</c> (registrado por
/// <c>AddSharedCaching</c>) no tiene ningún concepto de tenant propio — si dos tenants piden el mismo
/// recurso lógico con la misma clave literal, comparten la misma entrada cacheada. Estas pruebas
/// documentan primero el vector con <see cref="HybridCache"/> "en crudo" y después demuestran que
/// <see cref="ITenantAwareCache"/> (F1-16) lo cierra componiendo el <c>TenantId</c> en la clave.
/// </summary>
public class TenantAwareCacheTests
{
    private static ServiceProvider BuildProvider(Guid? tenantId, bool isMultiTenancyEnabled = true)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext(tenantId, isMultiTenancyEnabled));
        services.AddSharedCaching(configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Hallazgo de esta tarea: sin ningún mecanismo de tenant-scoping, dos tenants que usan la misma
    /// clave lógica ("literal") contra el mismo <see cref="HybridCache"/> comparten el valor cacheado
    /// — esto es exactamente lo que pasaría si un handler cacheara con
    /// <c>cache.GetOrCreateAsync("producto:slug-x", ...)</c> sin anteponer el tenant. No es un bug de
    /// HybridCache: es el comportamiento esperado de cualquier cache de clave/valor sin tenant en la
    /// clave, y es la razón por la que <see cref="ITenantAwareCache"/> existe.
    /// </summary>
    [Fact]
    public async Task RawHybridCache_WithSameLiteralKey_LeaksValueAcrossTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var provider = BuildProvider(tenantA);
        var rawCache = provider.GetRequiredService<HybridCache>();
        const string sharedLogicalKey = "producto:slug-compartido";

        var valueAsIfTenantA = await rawCache.GetOrCreateAsync(
            sharedLogicalKey, _ => ValueTask.FromResult("dato-de-tenant-A"));

        var factoryCalledAsIfTenantB = false;
        var valueAsIfTenantB = await rawCache.GetOrCreateAsync(sharedLogicalKey, _ =>
        {
            factoryCalledAsIfTenantB = true;
            return ValueTask.FromResult("dato-de-tenant-B");
        });

        valueAsIfTenantA.Should().Be("dato-de-tenant-A");
        valueAsIfTenantB.Should().Be("dato-de-tenant-A",
            "sin tenant-scoping en la clave, HybridCache no distingue entre tenantA y tenantB: esta es la fuga que ITenantAwareCache cierra");
        factoryCalledAsIfTenantB.Should().BeFalse();
        _ = tenantB; // documenta la intención del escenario aunque no se use como discriminador de clave
    }

    [Fact]
    public async Task TenantAwareCache_SameLogicalKey_DoesNotLeakBetweenTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSharedCaching(configuration);
        await using var provider = services.BuildServiceProvider();

        // Comparten la misma instancia de HybridCache (mismo proceso/L1) — solo cambia el
        // ITenantContext resuelto en cada scope, tal como pasaría con dos requests HTTP de tenants
        // distintos contra la misma instancia de la API.
        const string sharedLogicalKey = "producto:slug-compartido";

        await using (var scopeA = provider.CreateAsyncScope())
        {
            var cacheA = new TenantAwareCache(
                scopeA.ServiceProvider.GetRequiredService<HybridCache>(),
                new FakeTenantContext(tenantA));
            var valueForA = await cacheA.GetOrCreateAsync(sharedLogicalKey, _ => ValueTask.FromResult("dato-de-tenant-A"));
            valueForA.Should().Be("dato-de-tenant-A");
        }

        await using var scopeB = provider.CreateAsyncScope();
        var cacheB = new TenantAwareCache(
            scopeB.ServiceProvider.GetRequiredService<HybridCache>(),
            new FakeTenantContext(tenantB));

        var factoryCalledForB = false;
        var valueForB = await cacheB.GetOrCreateAsync(sharedLogicalKey, _ =>
        {
            factoryCalledForB = true;
            return ValueTask.FromResult("dato-de-tenant-B");
        });

        valueForB.Should().Be("dato-de-tenant-B",
            "tenant B nunca debe recibir el valor cacheado por tenant A para la misma clave lógica");
        factoryCalledForB.Should().BeTrue(
            "la clave efectiva de tenant B es distinta de la de tenant A (incluye su propio TenantId), así que debe recalcularse");
    }

    [Fact]
    public async Task TenantAwareCache_SameTenant_ReusesCachedValue_WithoutRecalculating()
    {
        var tenantId = Guid.NewGuid();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext(tenantId));
        services.AddSharedCaching(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ITenantAwareCache>();

        await cache.GetOrCreateAsync("producto:slug-x", _ => ValueTask.FromResult("dato-original"));

        var factoryCalledAgain = false;
        var secondRead = await cache.GetOrCreateAsync("producto:slug-x", _ =>
        {
            factoryCalledAgain = true;
            return ValueTask.FromResult("no-debería-usarse");
        });

        secondRead.Should().Be("dato-original");
        factoryCalledAgain.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrCreateAsync_MultiTenancyDisabled_UsesKeyUnscoped()
    {
        // Proyecto de un único tenant (NullTenantProvider/ITenantContext con IsMultiTenancyEnabled
        // == false): no hay ningún otro tenant del que aislar la clave, así que queda sin modificar.
        await using var provider = BuildProvider(tenantId: null, isMultiTenancyEnabled: false);
        await using var scope = provider.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ITenantAwareCache>();

        var value = await cache.GetOrCreateAsync("config:global", _ => ValueTask.FromResult("valor-compartido"));

        value.Should().Be("valor-compartido");
    }

    [Fact]
    public async Task GetOrCreateAsync_MultiTenancyEnabledWithoutResolvedTenantId_ThrowsInsteadOfCachingAmbiguously()
    {
        // Fallar de forma segura (mismo principio que HttpContextTenantProvider, F1-12): cachear con
        // una clave "sin tenant" en un proyecto multi-tenant sería tan peligroso como no filtrar una
        // query — cualquier scope sin tenant resuelto (por ejemplo, un bug) terminaría compartiendo
        // caché con todos los demás en la misma situación.
        await using var provider = BuildProvider(tenantId: null, isMultiTenancyEnabled: true);
        await using var scope = provider.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ITenantAwareCache>();

        var act = async () => await cache.GetOrCreateAsync("producto:slug-x", _ => ValueTask.FromResult("no-debería-cachearse"));

        await act.Should().ThrowAsync<TenantResolutionException>();
    }

    [Fact]
    public async Task RemoveAsync_OnlyRemovesEntryForCurrentTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSharedCaching(configuration);
        await using var provider = services.BuildServiceProvider();
        const string sharedLogicalKey = "producto:slug-compartido";

        await using (var scopeA = provider.CreateAsyncScope())
        {
            var cacheA = new TenantAwareCache(scopeA.ServiceProvider.GetRequiredService<HybridCache>(), new FakeTenantContext(tenantA));
            await cacheA.GetOrCreateAsync(sharedLogicalKey, _ => ValueTask.FromResult("dato-de-tenant-A"));
        }

        await using (var scopeB = provider.CreateAsyncScope())
        {
            var cacheB = new TenantAwareCache(scopeB.ServiceProvider.GetRequiredService<HybridCache>(), new FakeTenantContext(tenantB));
            await cacheB.GetOrCreateAsync(sharedLogicalKey, _ => ValueTask.FromResult("dato-de-tenant-B"));
        }

        await using (var scopeARemove = provider.CreateAsyncScope())
        {
            var cacheA = new TenantAwareCache(scopeARemove.ServiceProvider.GetRequiredService<HybridCache>(), new FakeTenantContext(tenantA));
            await cacheA.RemoveAsync(sharedLogicalKey);
        }

        await using var scopeBRead = provider.CreateAsyncScope();
        var cacheBRead = new TenantAwareCache(scopeBRead.ServiceProvider.GetRequiredService<HybridCache>(), new FakeTenantContext(tenantB));
        var factoryCalledForB = false;
        var valueForB = await cacheBRead.GetOrCreateAsync(sharedLogicalKey, _ =>
        {
            factoryCalledForB = true;
            return ValueTask.FromResult("dato-de-tenant-B");
        });

        valueForB.Should().Be("dato-de-tenant-B");
        factoryCalledForB.Should().BeFalse("eliminar la entrada de tenant A no debe afectar la de tenant B");
    }
}
