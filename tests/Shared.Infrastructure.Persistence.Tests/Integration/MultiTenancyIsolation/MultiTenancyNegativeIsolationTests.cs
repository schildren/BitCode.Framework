using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration.MultiTenancyIsolation;

/// <summary>
/// F1-16 — pruebas negativas de aislamiento contra SQL Server real (Testcontainers), complemento de
/// las ya existentes en <see cref="MultiTenantDbContextIntegrationTests"/> (F1-12: listado y acceso
/// puntual por Id). Estas cubren lo que F1-12 todavía no probaba explícitamente: el camino real que
/// sigue un <c>Actualizar{Entidad}Command</c>/<c>Eliminar{Entidad}Command</c> (fetch-then-mutate vía
/// <c>IRepository.GetByIdAsync</c>, tal como exige <c>docs/convenciones.md</c> — ningún handler recibe
/// un <c>DbContext</c> inyectado ni hace SQL directo) y consultas paginadas/filtradas que no
/// referencian el tenant explícitamente pero igual deben quedar acotadas por el filtro global.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiTenancyNegativeIsolationTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("BitCodeFramework", testName);

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString, ITenantProvider tenantProvider)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => tenantProvider);
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>().Database.EnsureCreatedAsync();

        return provider;
    }

    /// <summary>
    /// El único camino legítimo para actualizar una entidad (F1-07/convenciones regla 1: un handler
    /// nunca recibe el <c>DbContext</c>, solo <c>IRepository</c>) es leerla primero vía
    /// <c>GetByIdAsync</c> y mutar las propiedades del objeto ya trackeado. Si esa lectura ya devuelve
    /// <see langword="null"/> para una entidad de otro tenant (F1-12), la actualización nunca llega a
    /// ejecutarse — el handler real devuelve <c>Result.Failure</c> "NoEncontrado" antes de tocar el
    /// repositorio de escritura. Esta prueba fija ese comportamiento de punta a punta.
    /// </summary>
    [Fact]
    public async Task Update_OfOtherTenantEntity_NeverReachesWriteAfterNullFetch()
    {
        var connectionString = BuildIsolatedConnectionString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var entityIdOfTenantB = Guid.NewGuid();

        await using (var providerB = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantB)))
        await using (var scopeB = providerB.CreateAsyncScope())
        {
            var repo = scopeB.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
            var uow = scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.AddAsync(new TestEntity(entityIdOfTenantB, "Original-De-B", 50) { TenantId = tenantB });
            await uow.SaveChangesAsync();
        }

        await using var providerA = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantA));
        await using var scopeA = providerA.CreateAsyncScope();
        var repositoryA = scopeA.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
        var unitOfWorkA = scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var entityToUpdate = await repositoryA.GetByIdAsync(entityIdOfTenantB);
        entityToUpdate.Should().BeNull("un ActualizarCommand real consulta primero por Id; si no existe para este tenant, debe fallar como NotFound antes de mutar nada");

        // No hay nada que mutar/actualizar: el handler real terminaría acá con Result.Failure. Se
        // confirma además que no queda ningún cambio pendiente que un SaveChanges accidental pudiera
        // persistir.
        await unitOfWorkA.SaveChangesAsync();

        await using var verifyProvider = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantB));
        await using var verifyScope = verifyProvider.CreateAsyncScope();
        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
        var stillOriginal = await verifyRepo.GetByIdAsync(entityIdOfTenantB);
        stillOriginal.Should().NotBeNull();
        stillOriginal!.Name.Should().Be("Original-De-B", "el intento de actualización desde tenant A no debe haber alterado el registro de tenant B");
    }

    /// <summary>
    /// Mismo razonamiento que el caso de Update, aplicado al camino de un
    /// <c>Eliminar{Entidad}Command</c> real (fetch-then-remove).
    /// </summary>
    [Fact]
    public async Task Remove_OfOtherTenantEntity_NeverReachesWriteAfterNullFetch()
    {
        var connectionString = BuildIsolatedConnectionString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var entityIdOfTenantB = Guid.NewGuid();

        await using (var providerB = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantB)))
        await using (var scopeB = providerB.CreateAsyncScope())
        {
            var repo = scopeB.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
            var uow = scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.AddAsync(new TestEntity(entityIdOfTenantB, "No-Debe-Borrarse", 50) { TenantId = tenantB });
            await uow.SaveChangesAsync();
        }

        await using var providerA = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantA));
        await using var scopeA = providerA.CreateAsyncScope();
        var repositoryA = scopeA.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();

        var entityToRemove = await repositoryA.GetByIdAsync(entityIdOfTenantB);
        entityToRemove.Should().BeNull("un EliminarCommand real consulta primero por Id; si no existe para este tenant, debe fallar como NotFound antes de borrar nada");

        await using var verifyProvider = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantB));
        await using var verifyScope = verifyProvider.CreateAsyncScope();
        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
        var stillThere = await verifyRepo.GetByIdAsync(entityIdOfTenantB);
        stillThere.Should().NotBeNull("el intento de eliminación desde tenant A no debe haber afectado el registro de tenant B");
    }

    /// <summary>
    /// Extiende la prueba de listado de F1-12 (que solo usaba <c>ToListAsync</c> directo sobre el
    /// <c>DbSet</c>) al camino real que usaría un <c>Listar{Entidad}sQuery</c>: <c>IRepository.ListAsync</c>
    /// sin especificación (equivalente a "traer todo", el caso más peligroso de "adivinar" datos de
    /// otro tenant por ausencia de filtro explícito).
    /// </summary>
    [Fact]
    public async Task ListAsync_ViaRepository_WithoutExplicitFilter_OnlyReturnsCurrentTenantRows()
    {
        var connectionString = BuildIsolatedConnectionString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var providerA = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantA)))
        await using (var scopeA = providerA.CreateAsyncScope())
        {
            var repo = scopeA.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
            var uow = scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.AddAsync(new TestEntity(Guid.NewGuid(), "A-1", 10) { TenantId = tenantA });
            await repo.AddAsync(new TestEntity(Guid.NewGuid(), "A-2", 20) { TenantId = tenantA });
            await uow.SaveChangesAsync();
        }

        await using (var providerB = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantB)))
        await using (var scopeB = providerB.CreateAsyncScope())
        {
            var repo = scopeB.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
            var uow = scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.AddAsync(new TestEntity(Guid.NewGuid(), "B-1", 30) { TenantId = tenantB });
            await uow.SaveChangesAsync();
        }

        await using var readProviderA = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantA));
        await using var readScopeA = readProviderA.CreateAsyncScope();
        var repositoryA = readScopeA.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();

        var allVisibleToA = await repositoryA.ListAsync();

        allVisibleToA.Should().HaveCount(2);
        allVisibleToA.Should().OnlyContain(e => e.Name == "A-1" || e.Name == "A-2");
    }

    /// <summary>
    /// Una especificación que no menciona el tenant en absoluto (<see cref="ByAmountAboveSpecification"/>
    /// solo filtra por <c>Amount</c>) igual debe quedar acotada por el filtro global — el criterio de
    /// negocio de la specification se compone con el filtro de tenant, nunca lo reemplaza ni lo
    /// "abre". Confirma que <c>ListAsync</c>/<c>CountAsync</c>/<c>AnyAsync</c> con
    /// <see cref="ISpecification{T}"/> no son una forma de eludir el aislamiento.
    /// </summary>
    [Fact]
    public async Task ListAsync_WithSpecification_StillAppliesGlobalTenantFilter()
    {
        var connectionString = BuildIsolatedConnectionString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var providerA = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantA)))
        await using (var scopeA = providerA.CreateAsyncScope())
        {
            var repo = scopeA.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
            var uow = scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.AddAsync(new TestEntity(Guid.NewGuid(), "A-Caro", 1000) { TenantId = tenantA });
            await uow.SaveChangesAsync();
        }

        await using (var providerB = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantB)))
        await using (var scopeB = providerB.CreateAsyncScope())
        {
            var repo = scopeB.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
            var uow = scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>();
            // Mismo umbral de Amount que el de tenant A: si el filtro de tenant no se compusiera con
            // el de la specification, tenant A vería también esta fila de tenant B.
            await repo.AddAsync(new TestEntity(Guid.NewGuid(), "B-Caro", 2000) { TenantId = tenantB });
            await uow.SaveChangesAsync();
        }

        await using var readProviderA = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantA));
        await using var readScopeA = readProviderA.CreateAsyncScope();
        var repositoryA = readScopeA.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
        var specification = new ByAmountAboveSpecification(threshold: 500);

        var result = await repositoryA.ListAsync(specification);
        var count = await repositoryA.CountAsync(specification);
        var any = await repositoryA.AnyAsync(specification);

        result.Should().ContainSingle(e => e.Name == "A-Caro");
        result.Should().NotContain(e => e.Name == "B-Caro");
        count.Should().Be(1);
        any.Should().BeTrue();
    }

    /// <summary>
    /// Un acceso puntual por Id a una entidad de otro tenant no debe distinguirse — ni por excepción
    /// ni por código de error diferente — de un Id que directamente no existe en ningún tenant. Si
    /// difiriera, ya sería una fuga de información (confirmaría la existencia del registro).
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_OfOtherTenantEntity_IsIndistinguishableFromANonExistentId()
    {
        var connectionString = BuildIsolatedConnectionString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var entityIdOfTenantB = Guid.NewGuid();
        var completelyUnknownId = Guid.NewGuid();

        await using (var providerB = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantB)))
        await using (var scopeB = providerB.CreateAsyncScope())
        {
            var repo = scopeB.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
            var uow = scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.AddAsync(new TestEntity(entityIdOfTenantB, "Secreto-De-B", 5) { TenantId = tenantB });
            await uow.SaveChangesAsync();
        }

        await using var providerA = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantA));
        await using var scopeA = providerA.CreateAsyncScope();
        var repositoryA = scopeA.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();

        Func<Task<TestEntity?>> fetchOtherTenantEntity = () => repositoryA.GetByIdAsync(entityIdOfTenantB);
        Func<Task<TestEntity?>> fetchUnknownEntity = () => repositoryA.GetByIdAsync(completelyUnknownId);

        var resultForOtherTenant = await fetchOtherTenantEntity.Should().NotThrowAsync();
        var resultForUnknown = await fetchUnknownEntity.Should().NotThrowAsync();

        resultForOtherTenant.Subject.Should().BeNull();
        resultForUnknown.Subject.Should().BeNull();
    }
}
