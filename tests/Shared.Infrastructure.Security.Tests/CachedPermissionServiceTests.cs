using BitCode.Framework.Shared.Infrastructure.Caching;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

/// <summary>
/// F2-09: prueba de componente contra <see cref="HybridCache"/> REAL (L1 en memoria, sin mocks) --
/// solo <see cref="IPermissionService"/> (la fuente de SQL Server) está sustituido, exactamente como
/// <c>PermissionEvaluatorTests</c> sustituye <c>IPermissionService</c> pero deja el resto de la cadena
/// real. La capa L2/Redis se prueba por separado en
/// <c>Integration/CachedPermissionServiceRedisIntegrationTests.cs</c>.
/// </summary>
public sealed class CachedPermissionServiceTests : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly HybridCache _hybridCache;

    public CachedPermissionServiceTests()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        _provider = services.BuildServiceProvider();
        _hybridCache = _provider.GetRequiredService<HybridCache>();
    }

    private CachedPermissionService CreateSut(
        IPermissionService inner,
        Guid? tenantId = null,
        bool multiTenancyEnabled = false,
        TimeSpan? expiration = null)
    {
        var tenantProvider = new FakeTenantProvider(tenantId, multiTenancyEnabled);
        var tenantContext = new TenantContext(tenantProvider);
        var tenantAwareCache = new TenantAwareCache(_hybridCache, tenantContext);
        var options = Options.Create(new PermissionCacheOptions
        {
            Expiration = expiration ?? TimeSpan.FromMinutes(5),
        });

        return new CachedPermissionService(inner, tenantAwareCache, _hybridCache, options);
    }

    // ---- GetPermissionsForUserAsync: hit / miss ----

    [Fact]
    public async Task GetPermissionsForUserAsync_FirstCall_IsCacheMissAndCallsInner()
    {
        var inner = Substitute.For<IPermissionService>();
        var userId = Guid.NewGuid();
        inner.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.crear"]));
        var sut = CreateSut(inner);

        var result = await sut.GetPermissionsForUserAsync(userId);

        result.Should().BeEquivalentTo(["productos.crear"]);
        await inner.Received(1).GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPermissionsForUserAsync_SecondCallWithinTtl_IsCacheHitAndDoesNotCallInnerAgain()
    {
        var inner = Substitute.For<IPermissionService>();
        var userId = Guid.NewGuid();
        inner.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.crear"]));
        var sut = CreateSut(inner);

        await sut.GetPermissionsForUserAsync(userId);
        var second = await sut.GetPermissionsForUserAsync(userId);

        second.Should().BeEquivalentTo(["productos.crear"]);
        await inner.Received(1).GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPermissionsForUserAsync_DifferentUsers_AreCachedIndependently()
    {
        var inner = Substitute.For<IPermissionService>();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        inner.GetPermissionsForUserAsync(userA, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.crear"]));
        inner.GetPermissionsForUserAsync(userB, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.eliminar"]));
        var sut = CreateSut(inner);

        var resultA = await sut.GetPermissionsForUserAsync(userA);
        var resultB = await sut.GetPermissionsForUserAsync(userB);

        resultA.Should().BeEquivalentTo(["productos.crear"]);
        resultB.Should().BeEquivalentTo(["productos.eliminar"]);
        await inner.Received(1).GetPermissionsForUserAsync(userA, Arg.Any<CancellationToken>());
        await inner.Received(1).GetPermissionsForUserAsync(userB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPermissionsForUserAsync_MultiTenant_SameUserIdDifferentTenants_DoesNotShareCacheEntry()
    {
        // Regla dura #14 (docs/convenciones.md, F1-16): datos sensibles a tenant nunca comparten clave
        // de cache entre tenants -- este es el caso "mismo Guid de usuario, dos tenants distintos".
        var inner = Substitute.For<IPermissionService>();
        var userId = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        inner.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.crear"]));

        var sutTenantA = CreateSut(inner, tenantA, multiTenancyEnabled: true);
        var sutTenantB = CreateSut(inner, tenantB, multiTenancyEnabled: true);

        await sutTenantA.GetPermissionsForUserAsync(userId);
        await sutTenantB.GetPermissionsForUserAsync(userId);

        await inner.Received(2).GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPermissionsForUserAsync_AfterExpiration_CallsInnerAgain()
    {
        var inner = Substitute.For<IPermissionService>();
        var userId = Guid.NewGuid();
        inner.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.crear"]));
        var sut = CreateSut(inner, expiration: TimeSpan.FromMilliseconds(50));

        await sut.GetPermissionsForUserAsync(userId);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await sut.GetPermissionsForUserAsync(userId);

        await inner.Received(2).GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateUserAsync_RemovesCachedEntry_NextCallHitsInnerAgain()
    {
        var inner = Substitute.For<IPermissionService>();
        var userId = Guid.NewGuid();
        inner.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.crear"]));
        var sut = CreateSut(inner);

        await sut.GetPermissionsForUserAsync(userId);
        await sut.InvalidateUserAsync(userId);
        await sut.GetPermissionsForUserAsync(userId);

        await inner.Received(2).GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateUserAsync_MultiTenant_OnlyInvalidatesCallingTenantEntry()
    {
        var inner = Substitute.For<IPermissionService>();
        var userId = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        inner.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.crear"]));

        var sutTenantA = CreateSut(inner, tenantA, multiTenancyEnabled: true);
        var sutTenantB = CreateSut(inner, tenantB, multiTenancyEnabled: true);

        await sutTenantA.GetPermissionsForUserAsync(userId);
        await sutTenantB.GetPermissionsForUserAsync(userId);
        await sutTenantA.InvalidateUserAsync(userId);

        // Tenant A recalcula (invalidado); tenant B sigue en cache (no se tocó su entrada).
        await sutTenantA.GetPermissionsForUserAsync(userId);
        await sutTenantB.GetPermissionsForUserAsync(userId);

        await inner.Received(3).GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    // ---- GetPermissionsForRoleAsync: hit / miss ----

    [Fact]
    public async Task GetPermissionsForRoleAsync_FirstCall_IsCacheMissAndCallsInner()
    {
        var inner = Substitute.For<IPermissionService>();
        inner.GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.editar"]));
        var sut = CreateSut(inner);

        var result = await sut.GetPermissionsForRoleAsync("Editor");

        result.Should().BeEquivalentTo(["productos.editar"]);
        await inner.Received(1).GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_SecondCallWithinTtl_IsCacheHitAndDoesNotCallInnerAgain()
    {
        var inner = Substitute.For<IPermissionService>();
        inner.GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.editar"]));
        var sut = CreateSut(inner);

        await sut.GetPermissionsForRoleAsync("Editor");
        await sut.GetPermissionsForRoleAsync("Editor");

        await inner.Received(1).GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_AfterExpiration_CallsInnerAgain()
    {
        var inner = Substitute.For<IPermissionService>();
        inner.GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.editar"]));
        var sut = CreateSut(inner, expiration: TimeSpan.FromMilliseconds(50));

        await sut.GetPermissionsForRoleAsync("Editor");
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await sut.GetPermissionsForRoleAsync("Editor");

        await inner.Received(2).GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateRoleAsync_RemovesCachedEntry_NextCallHitsInnerAgain()
    {
        var inner = Substitute.For<IPermissionService>();
        inner.GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.editar"]));
        var sut = CreateSut(inner);

        await sut.GetPermissionsForRoleAsync("Editor");
        await sut.InvalidateRoleAsync("Editor");
        await sut.GetPermissionsForRoleAsync("Editor");

        await inner.Received(2).GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_NotTenantScoped_SharesCacheEntryAcrossTenants()
    {
        // Decisión de diseño documentada (docs/guia-rbac-2.md): ApplicationRole todavía no implementa
        // ITenantEntity, así que los permisos de un rol NO son datos sensibles a tenant hoy -- dos
        // "tenants" (dos scopes con ITenantContext distinto) que consultan el mismo nombre de rol deben
        // compartir la misma entrada cacheada (a diferencia de GetPermissionsForUserAsync).
        var inner = Substitute.For<IPermissionService>();
        inner.GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.editar"]));

        var sutTenantA = CreateSut(inner, Guid.NewGuid(), multiTenancyEnabled: true);
        var sutTenantB = CreateSut(inner, Guid.NewGuid(), multiTenancyEnabled: true);

        await sutTenantA.GetPermissionsForRoleAsync("Editor");
        await sutTenantB.GetPermissionsForRoleAsync("Editor");

        await inner.Received(1).GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>());
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
}
