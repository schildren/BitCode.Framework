using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Security;
using BitCode.Framework.Shared.Infrastructure.Persistence.Repositories;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// Cierra la Fase 1 verificando contra un SQL Server real (Testcontainers) lo que las Tareas
/// 1.1-1.7 solo probaron contra InMemory/SQLite: CRUD vía repositorio genérico, auditoría
/// automática, soft-delete, aislamiento multi-tenant y transacciones, todo cableado a través de
/// AddSharedPersistence tal como lo usaría un proyecto consumidor real.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiTenantDbContextIntegrationTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("BitCodeFramework", testName);

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString, ITenantProvider? tenantProvider = null)
    {
        var services = new ServiceCollection();
        if (tenantProvider is not null)
        {
            services.AddScoped(_ => tenantProvider);
        }

        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        return provider;
    }

    [Fact]
    public async Task Repository_AddAndRetrieve_PersistsToRealSqlServer()
    {
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);

        await repository.AddAsync(entity);
        await unitOfWork.SaveChangesAsync();

        var found = await repository.GetByIdAsync(entity.Id);
        found.Should().NotBeNull();
        found!.Name.Should().Be("Alpha");
    }

    [Fact]
    public async Task Repository_Add_PopulatesAuditFieldsViaInterceptor()
    {
        var connectionString = BuildIsolatedConnectionString();
        var services = new ServiceCollection();
        services.AddScoped<ICurrentUserProvider>(_ => new FakeCurrentUserProvider("integration-user"));
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        await using var provider = services.BuildServiceProvider();

        await using (var setupScope = provider.CreateAsyncScope())
        {
            await setupScope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>().Database.EnsureCreatedAsync();
        }

        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);

        await repository.AddAsync(entity);
        await unitOfWork.SaveChangesAsync();

        entity.CreatedAtUtc.Should().NotBe(default);
        entity.CreatedBy.Should().Be("integration-user");
    }

    [Fact]
    public async Task Repository_Remove_SoftDeletesAndIsExcludedFromDefaultQueries()
    {
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);
        await repository.AddAsync(entity);
        await unitOfWork.SaveChangesAsync();

        repository.Remove(entity);
        await unitOfWork.SaveChangesAsync();

        var found = await repository.GetByIdAsync(entity.Id);
        found.Should().BeNull("el filtro global debe ocultar el registro soft-eliminado");

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var physicallyPresent = await context.TestEntities.IgnoreQueryFilters().AnyAsync(e => e.Id == entity.Id);
        physicallyPresent.Should().BeTrue("el soft-delete no debe borrar físicamente la fila");
    }

    [Fact]
    public async Task MultiTenancy_IsolatesDataBetweenTenantsOnSameDatabase()
    {
        var connectionString = BuildIsolatedConnectionString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var providerA = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantA)))
        await using (var scopeA = providerA.CreateAsyncScope())
        {
            var repo = scopeA.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
            var uow = scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.AddAsync(new TestEntity(Guid.NewGuid(), "Entidad-A", 10) { TenantId = tenantA });
            await uow.SaveChangesAsync();
        }

        await using (var providerB = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantB)))
        await using (var scopeB = providerB.CreateAsyncScope())
        {
            var repo = scopeB.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
            var uow = scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.AddAsync(new TestEntity(Guid.NewGuid(), "Entidad-B", 20) { TenantId = tenantB });
            await uow.SaveChangesAsync();
        }

        await using var readProviderA = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantA));
        await using var readScopeA = readProviderA.CreateAsyncScope();
        var contextA = readScopeA.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var visibleToA = await contextA.TestEntities.ToListAsync();

        visibleToA.Should().ContainSingle(e => e.Name == "Entidad-A");
        visibleToA.Should().NotContain(e => e.Name == "Entidad-B");
    }

    [Fact]
    public async Task UnitOfWork_Rollback_DiscardsChangesAgainstRealSqlServer()
    {
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);

        await unitOfWork.BeginTransactionAsync();
        await repository.AddAsync(entity);
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.RollbackAsync();

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var found = await context.TestEntities.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == entity.Id);
        found.Should().BeNull();
    }
}
