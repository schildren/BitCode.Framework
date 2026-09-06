using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class PersistenceServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddSharedPersistence_ResolvesRepositoryUnitOfWorkAndDefaultProviders()
    {
        var services = new ServiceCollection();
        services.AddSharedPersistence<MultiTenantTestDbContext>(
            "Server=(localdb)\\mssqllocaldb;Database=BitCodeFrameworkTests;Trusted_Connection=True;");

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();

        repository.Should().NotBeNull();
        unitOfWork.Should().NotBeNull();
        tenantProvider.Should().BeOfType<MultiTenancy.NullTenantProvider>();
    }

    [Fact]
    public async Task AddSharedPersistence_ResolvesIReadRepository()
    {
        // Regresión: el proyecto piloto Sample.Api (Fase 8) detectó que un handler de IQuery
        // (Fase 2), que solo necesita lectura, no podía inyectar IReadRepository<,> — únicamente
        // IRepository<,> quedaba resoluble. Desde F1-17, IReadRepository<,> resuelve a
        // ReadOnlyRepositoryBase<,> (no a RepositoryBase<,>, que sigue detrás de IRepository<,>
        // para el lado de escritura) — ver PersistenceServiceCollectionExtensionsTests_
        // ResolvesIReadRepositoryAsReadOnlyImplementation más abajo para el porqué.
        var services = new ServiceCollection();
        services.AddSharedPersistence<MultiTenantTestDbContext>(
            "Server=(localdb)\\mssqllocaldb;Database=BitCodeFrameworkTests;Trusted_Connection=True;");

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var readRepository = scope.ServiceProvider.GetRequiredService<IReadRepository<TestEntity, Guid>>();

        readRepository.Should().NotBeNull();
    }

    [Fact]
    public async Task AddSharedPersistence_ResolvesIReadRepositoryAsReadOnlyImplementation()
    {
        // F1-17: IReadRepository<,> debe resolver a ReadOnlyRepositoryBase<,> (fuerza AsNoTracking
        // en toda lectura), no a RepositoryBase<,> (que sigue trackeando para el lado de escritura,
        // detrás de IRepository<,>) — ambas interfaces ya no comparten la misma clase concreta.
        var services = new ServiceCollection();
        services.AddSharedPersistence<MultiTenantTestDbContext>(
            "Server=(localdb)\\mssqllocaldb;Database=BitCodeFrameworkTests;Trusted_Connection=True;");

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var readRepository = scope.ServiceProvider.GetRequiredService<IReadRepository<TestEntity, Guid>>();
        var writeRepository = scope.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();

        readRepository.Should().BeOfType<ReadOnlyRepositoryBase<TestEntity, Guid>>();
        writeRepository.Should().BeOfType<RepositoryBase<TestEntity, Guid>>();
    }

    [Fact]
    public async Task AddSharedPersistence_RespectsTenantProviderRegisteredBeforehand()
    {
        var services = new ServiceCollection();
        var tenantId = Guid.NewGuid();
        services.AddScoped<ITenantProvider>(_ => new FakeTenantProvider(tenantId));

        services.AddSharedPersistence<MultiTenantTestDbContext>(
            "Server=(localdb)\\mssqllocaldb;Database=BitCodeFrameworkTests;Trusted_Connection=True;");

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();

        tenantProvider.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task AddSharedPersistence_ResolvesDefaultShardContracts_PreservingT1Behavior()
    {
        // F1-13: por defecto (sin que el proyecto consumidor opte por T2), IShardResolver siempre
        // resuelve al shard compartido e IShardConnectionStringProvider siempre devuelve la única
        // connectionString configurada — cero cambio de comportamiento respecto de T1 (F1-12).
        const string connectionString =
            "Server=(localdb)\\mssqllocaldb;Database=BitCodeFrameworkTests;Trusted_Connection=True;";
        var services = new ServiceCollection();
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var shardResolver = scope.ServiceProvider.GetRequiredService<IShardResolver>();
        var shardConnectionStringProvider = scope.ServiceProvider.GetRequiredService<IShardConnectionStringProvider>();

        var resolvedShard = await shardResolver.ResolveShardAsync(Guid.NewGuid(), CancellationToken.None);
        var resolvedConnectionString = await shardConnectionStringProvider.GetConnectionStringAsync(
            resolvedShard, CancellationToken.None);

        resolvedShard.Should().Be(ShardId.Shared);
        resolvedConnectionString.Should().Be(connectionString);
    }
}
