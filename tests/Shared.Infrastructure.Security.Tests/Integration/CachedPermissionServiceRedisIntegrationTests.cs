using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Caching;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Integration;

/// <summary>
/// F2-09: cierra el criterio de aceptación ("sin consulta SQL por request normal") contra un Redis REAL
/// (Testcontainers) -- exactamente el mismo motivo que
/// <c>Shared.Infrastructure.Caching.Tests/Integration/HybridCacheRedisIntegrationTests.cs</c> (F1-16):
/// dos <see cref="ServiceProvider"/> independientes, cada uno con su propia capa L1 vacía, simulan dos
/// instancias del proceso que comparten la capa L2 (Redis).
/// </summary>
[Collection(RedisCollection.Name)]
public class CachedPermissionServiceRedisIntegrationTests(RedisContainerFixture fixture)
{
    private CachedPermissionService BuildSut(IPermissionService inner, Guid? tenantId = null, bool multiTenancyEnabled = false)
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

        var hybridCache = provider.GetRequiredService<HybridCache>();
        var tenantContext = new TenantContext(new FakeTenantProvider(tenantId, multiTenancyEnabled));
        var tenantAwareCache = new TenantAwareCache(hybridCache, tenantContext);
        var options = Options.Create(new PermissionCacheOptions { Expiration = TimeSpan.FromMinutes(5) });

        return new CachedPermissionService(inner, tenantAwareCache, hybridCache, options);
    }

    [Fact]
    public async Task GetPermissionsForUserAsync_CachedByOneInstance_IsVisibleToAnotherInstance_ViaRedisL2()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var inner = Substitute.For<IPermissionService>();
        inner.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.crear"]));

        var instanceA = BuildSut(inner, tenantId, multiTenancyEnabled: true);
        var instanceB = BuildSut(inner, tenantId, multiTenancyEnabled: true);

        await instanceA.GetPermissionsForUserAsync(userId);
        await Task.Delay(TimeSpan.FromSeconds(1)); // escritura a Redis L2 es asíncrona (F1-16)

        var resultFromInstanceB = await instanceB.GetPermissionsForUserAsync(userId);

        resultFromInstanceB.Should().BeEquivalentTo(["productos.crear"]);
        // La L1 de instanceB estaba vacía: si llegó el valor correcto sin que inner se haya llamado una
        // segunda vez, tuvo que venir de Redis (L2), no recalcularse.
        await inner.Received(1).GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_CachedByOneInstance_IsVisibleToAnotherInstance_ViaRedisL2()
    {
        var inner = Substitute.For<IPermissionService>();
        inner.GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.editar"]));

        var instanceA = BuildSut(inner);
        var instanceB = BuildSut(inner);

        await instanceA.GetPermissionsForRoleAsync("Editor");
        await Task.Delay(TimeSpan.FromSeconds(1));

        var resultFromInstanceB = await instanceB.GetPermissionsForRoleAsync("Editor");

        resultFromInstanceB.Should().BeEquivalentTo(["productos.editar"]);
        await inner.Received(1).GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateUserAsync_RemovesEntryFromRedisL2_AFreshInstanceRecomputesInsteadOfReadingStaleValue()
    {
        var userId = Guid.NewGuid();
        var inner = Substitute.For<IPermissionService>();
        inner.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<string>>(["productos.crear"]),
                Task.FromResult<IReadOnlyList<string>>(["productos.crear", "productos.eliminar"]));

        var instanceA = BuildSut(inner);

        await instanceA.GetPermissionsForUserAsync(userId);
        await Task.Delay(TimeSpan.FromSeconds(1)); // deja completar la escritura asíncrona a Redis L2
        // antes de invalidar -- de lo contrario la escritura diferida del valor original podría
        // completarse DESPUÉS del borrado y dejar la entrada vieja en Redis igual (ver el comentario de
        // CachedPermissionService sobre el límite de correctitud multi-instancia).
        await instanceA.InvalidateUserAsync(userId);
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Instancia NUEVA con L1 vacía: si la invalidación realmente llegó a Redis (L2), esta instancia
        // no puede leer el valor viejo desde ningún lado y debe recalcular contra "inner".
        var freshInstance = BuildSut(inner);
        var result = await freshInstance.GetPermissionsForUserAsync(userId);

        result.Should().BeEquivalentTo(["productos.crear", "productos.eliminar"]);
        await inner.Received(2).GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>());
    }
}
