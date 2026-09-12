using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

/// <summary>
/// F2-09: registro DI de <see cref="PermissionCacheServiceCollectionExtensions.AddSharedPermissionCache"/>
/// -- decora el <see cref="IPermissionService"/> ya registrado sin romper <see cref="IPermissionEvaluator"/>
/// (que sigue dependiendo de la MISMA interfaz, F2-07, sin cambios).
/// </summary>
public class PermissionCacheServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSharedPermissionCache_WithoutIPermissionServiceRegistered_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedPermissionCache();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedPermissionCache_CalledAfterAddSharedPermissionEvaluation_DecoratesIPermissionService()
    {
        var services = new ServiceCollection();
        services.AddSharedPermissionEvaluation();

        services.AddSharedPermissionCache();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IPermissionService>().Should().BeOfType<CachedPermissionService>();
    }

    [Fact]
    public void AddSharedPermissionCache_ResolvesIPermissionServiceAndIPermissionCacheInvalidator_AsTheSameInstance()
    {
        var services = new ServiceCollection();
        services.AddSharedPermissionEvaluation();

        services.AddSharedPermissionCache();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var asPermissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var asInvalidator = scope.ServiceProvider.GetRequiredService<IPermissionCacheInvalidator>();

        asInvalidator.Should().BeSameAs(asPermissionService);
    }

    [Fact]
    public void AddSharedPermissionCache_AppliesConfigureOptionsDelegate()
    {
        var services = new ServiceCollection();
        services.AddSharedPermissionEvaluation();
        var expected = TimeSpan.FromSeconds(15);

        services.AddSharedPermissionCache(options => options.Expiration = expected);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<PermissionCacheOptions>>().Value.Expiration.Should().Be(expected);
    }

    [Fact]
    public void AddSharedPermissionCache_CalledTwice_DoesNotDoubleWrapIPermissionService()
    {
        var services = new ServiceCollection();
        services.AddSharedPermissionEvaluation();

        services.AddSharedPermissionCache();
        services.AddSharedPermissionCache();

        // Idempotente: un único registro de IPermissionCacheInvalidator "real" (con ImplementationFactory
        // propio del decorador) tras dos llamadas -- un doble envoltorio agregaría un segundo registro
        // reemplazando "sobre" el anterior en vez de dejar uno solo consistente.
        var invalidatorDescriptors = services.Count(d => d.ServiceType == typeof(IPermissionCacheInvalidator));
        invalidatorDescriptors.Should().Be(1);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IPermissionService>().Should().BeOfType<CachedPermissionService>();
    }

    [Fact]
    public async Task AddSharedPermissionCache_DecoratesCustomIPermissionServiceRegisteredDirectly()
    {
        var services = new ServiceCollection();
        var customInner = Substitute.For<IPermissionService>();
        var userId = Guid.NewGuid();
        customInner.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["productos.crear"]));

        services.AddSharedPermissionEvaluation();
        services.AddScoped(_ => customInner);

        services.AddSharedPermissionCache();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var decorated = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var first = await decorated.GetPermissionsForUserAsync(userId);
        var second = await decorated.GetPermissionsForUserAsync(userId);

        first.Should().BeEquivalentTo(["productos.crear"]);
        second.Should().BeEquivalentTo(["productos.crear"]);
        await customInner.Received(1).GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>());
    }
}
